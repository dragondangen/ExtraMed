using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var settings = new CardSettings(builder.Configuration, builder.Environment);
Uri? mobileOrigin = settings.MobileOrigin;

var keyDirectory = Directory.CreateDirectory(Path.Combine(settings.DataDirectory, "keys"));
builder.Services.AddDataProtection().SetApplicationName("ExtraMed").PersistKeysToFileSystem(keyDirectory);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<CardStore>();
builder.Services.AddAuthentication("Staff").AddCookie("Staff", options =>
{
    options.Cookie.Name = "ExtraMed.Staff";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = mobileOrigin is null ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(4);
    options.SlidingExpiration = false;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(2) }));
    options.AddPolicy("create", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(10) }));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6_000_000);

var app = builder.Build();
// With a reverse proxy, forwarded headers from loopback proxies are trusted by default.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.Use(async (context, next) =>
{
    bool privateRoute = context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/c") || context.Request.Path.StartsWithSegments("/staff");
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' blob:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    if (privateRoute)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    if (mobileOrigin is not null && !context.Request.IsHttps)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            var target = mobileOrigin.GetLeftPart(UriPartial.Authority) +
                context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent();
            context.Response.Redirect(target, permanent: false);
        }
        else
        {
            await Error(403, "Для этого запроса требуется HTTPS.").ExecuteAsync(context);
        }
        return;
    }
    if (mobileOrigin is not null) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";

    await next();
});
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();

app.MapGet("/", (HttpContext context) => Page("index.html", context, app.Environment));
app.MapGet("/c/{id}", (string id, HttpContext context) => Page("card.html", context, app.Environment));
app.MapGet("/staff", (HttpContext context) => Page("staff.html", context, app.Environment));

app.MapPost("/api/cards", async (HttpContext context, CardStore store, CancellationToken cancellation) =>
{
    // An application-specific request header and the absence of CORS prevent cross-origin saves.
    if (context.Request.Headers["X-ExtraMed"] != "1" || !context.Request.HasFormContentType)
        return Error(400, "Не удалось прочитать анкету.");

    IFormCollection form;
    try
    {
        form = await context.Request.ReadFormAsync(cancellation);
    }
    catch (InvalidDataException)
    {
        return Error(400, "Не удалось прочитать анкету. Проверьте размер фотографии.");
    }

    var validation = PatientInput.Parse(form);
    if (validation.Error is { } invalid) return Error(400, invalid.Message, invalid.Field);

    var photo = await PatientInput.ReadPhoto(form.Files.GetFile("photo"), cancellation);
    if (photo.Error is not null) return Error(400, photo.Error, "photo");

    var patient = validation.Patient!;
    if (settings.StaffPassword is null && (patient.Passport is not null || patient.Policy is not null))
        return Error(400, "Для сохранения документов администратор должен настроить вход для сотрудников.", "documents");

    var id = await store.CreateAsync(new StoredCard(patient, photo.ContentType, photo.Bytes), cancellation);
    var cardPath = $"/c/{id}";
    return Results.Created(cardPath, new { cardPath, nfcUrl = mobileOrigin is null ? null : new Uri(mobileOrigin, cardPath).AbsoluteUri });
}).RequireRateLimiting("create");

app.MapGet("/api/cards/{id}", async (string id, CardStore store, CancellationToken cancellation) =>
{
    var card = await store.GetAsync(id, cancellation);
    if (card is null) return Error(404, "Карточка не найдена.");

    var p = card.Patient;
    return Results.Ok(new
    {
        p.FullName, p.Sex, p.Age, p.Allergies, p.Medications, p.History, p.Pregnancy, p.PregnancyDetails,
        p.UpdatedAt, photo = card.PhotoBytes is null ? null : $"/api/cards/{id}/photo"
    });
});

app.MapGet("/api/cards/{id}/photo", async (string id, CardStore store, CancellationToken cancellation) =>
{
    var card = await store.GetAsync(id, cancellation);
    return card?.PhotoBytes is { } photo ? Results.File(photo, card.PhotoContentType) : Results.NotFound();
});

app.MapGet("/api/cards/{id}/documents", async (string id, HttpContext context, CardStore store, CancellationToken cancellation) =>
{
    if (settings.StaffPassword is null) return Error(503, "Вход для сотрудников пока не настроен.");
    if (context.User.Identity?.IsAuthenticated != true) return Error(401, "Войдите, чтобы посмотреть документы.");

    var card = await store.GetAsync(id, cancellation);
    return card is null
        ? Error(404, "Карточка не найдена.")
        : Results.Ok(new { card.Patient.Passport, card.Patient.Policy });
});

app.MapGet("/api/settings", () => Results.Ok(new { staffEnabled = settings.StaffPassword is not null }));

app.MapGet("/api/staff/session", (HttpContext context) => Results.Ok(new
{
    enabled = settings.StaffPassword is not null,
    signedIn = settings.StaffPassword is not null && context.User.Identity?.IsAuthenticated == true
}));

app.MapPost("/api/staff/login", async (LoginInput input, HttpContext context) =>
{
    if (context.Request.Headers["X-ExtraMed"] != "1") return Error(400, "Некорректный запрос.");
    if (settings.StaffPassword is null) return Error(503, "Вход для сотрудников пока не настроен.");
    if (!settings.VerifyPassword(input.Password)) return Error(401, "Неверный пароль.");

    await context.SignInAsync("Staff", CardSettings.StaffIdentity());
    return Results.NoContent();
}).RequireRateLimiting("login");

app.MapPost("/api/staff/logout", async (HttpContext context) =>
{
    if (context.Request.Headers["X-ExtraMed"] != "1") return Error(400, "Некорректный запрос.");
    await context.SignOutAsync("Staff");
    return Results.NoContent();
});

app.Run();

static IResult Error(int status, string message, string? field = null) =>
    Results.Json(new { message, field }, statusCode: status);

static IResult Page(string file, HttpContext context, IWebHostEnvironment environment)
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(Path.Combine(environment.ContentRootPath, "Views", file), "text/html; charset=utf-8");
}

record LoginInput(string? Password);

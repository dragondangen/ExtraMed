using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

internal sealed class CardSettings
{
    public Uri? MobileOrigin { get; }
    public string? StaffPassword { get; }
    public string DataDirectory { get; }
    public bool UseVercel { get; }
    public int PhotoLimitMb => UseVercel ? 4 : 5;

    public CardSettings(IConfiguration config, IWebHostEnvironment environment)
    {
        UseVercel = config.GetValue<bool>("ExtraMed:UseVercel") || config["VERCEL"] == "1";
        var configuredUrl = config["ExtraMed:PublicBaseUrl"];
        if (UseVercel && string.IsNullOrWhiteSpace(configuredUrl) && config["VERCEL_ENV"] == "production" &&
            config["VERCEL_PROJECT_PRODUCTION_URL"] is { Length: > 0 } productionHost)
            configuredUrl = $"https://{productionHost}";

        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                uri.IsLoopback || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                throw new InvalidOperationException("ExtraMed:PublicBaseUrl must be an HTTPS origin reachable from the phone.");
            MobileOrigin = uri;
        }

        StaffPassword = config["ExtraMed:StaffPassword"];
        if (string.IsNullOrEmpty(StaffPassword)) StaffPassword = null;
        if (StaffPassword is { Length: < 16 or > 256 })
            throw new InvalidOperationException("ExtraMed:StaffPassword must contain between 16 and 256 characters.");

        DataDirectory = Path.GetFullPath(config["ExtraMed:DataDirectory"] ?? "App_Data", environment.ContentRootPath);
        var webRelative = Path.GetRelativePath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), DataDirectory);
        if (webRelative != ".." && !webRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(webRelative))
            throw new InvalidOperationException("The ExtraMed data directory must be outside wwwroot.");
    }

    public bool VerifyPassword(string? password)
    {
        if (StaffPassword is null || string.IsNullOrEmpty(password) || password.Length > 256) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(StaffPassword)),
            SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    public static ClaimsPrincipal StaffIdentity() => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "staff")], "Staff"));
}

internal sealed record StoredCard(Patient Patient, string? PhotoContentType, byte[]? PhotoBytes);

internal sealed class CardStore(ICardStorage storage, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("ExtraMed.Cards.v1");

    public async Task<string> CreateAsync(StoredCard card, CancellationToken cancellation)
    {
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        var bytes = protector.CreateProtector(id).Protect(JsonSerializer.SerializeToUtf8Bytes(card));
        await storage.WriteAsync(id, bytes, cancellation);
        return id;
    }

    public async Task<StoredCard?> GetAsync(string id, CancellationToken cancellation)
    {
        if (id.Length != 32 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return null;

        var bytes = await storage.ReadAsync(id, cancellation);
        return bytes is null ? null : JsonSerializer.Deserialize<StoredCard>(protector.CreateProtector(id).Unprotect(bytes));
    }
}

internal interface ICardStorage
{
    Task WriteAsync(string id, byte[] bytes, CancellationToken cancellation);
    Task<byte[]?> ReadAsync(string id, CancellationToken cancellation);
}

internal sealed class FileCardStorage(CardSettings settings) : ICardStorage
{
    private readonly string directory = Directory.CreateDirectory(Path.Combine(settings.DataDirectory, "cards")).FullName;

    public async Task WriteAsync(string id, byte[] bytes, CancellationToken cancellation)
    {
        var tempPath = Path.Combine(directory, id + ".tmp");
        var finalPath = Path.Combine(directory, id + ".card");

        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellation);
            File.Move(tempPath, finalPath);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    public async Task<byte[]?> ReadAsync(string id, CancellationToken cancellation)
    {
        try
        {
            return await File.ReadAllBytesAsync(Path.Combine(directory, id + ".card"), cancellation);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

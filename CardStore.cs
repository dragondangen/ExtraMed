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

    public CardSettings(IConfiguration config, IWebHostEnvironment environment)
    {
        var configuredUrl = config["ExtraMed:PublicBaseUrl"];
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

internal sealed class CardStore(CardSettings settings, IDataProtectionProvider protection)
{
    private readonly string directory = Directory.CreateDirectory(Path.Combine(settings.DataDirectory, "cards")).FullName;
    private readonly IDataProtector protector = protection.CreateProtector("ExtraMed.Cards.v1");

    public async Task<string> CreateAsync(StoredCard card, CancellationToken cancellation)
    {
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        var tempPath = Path.Combine(directory, id + ".tmp");
        var finalPath = Path.Combine(directory, id + ".card");

        try
        {
            var bytes = protector.CreateProtector(id).Protect(JsonSerializer.SerializeToUtf8Bytes(card));
            await File.WriteAllBytesAsync(tempPath, bytes, cancellation);
            File.Move(tempPath, finalPath);
        }
        finally
        {
            File.Delete(tempPath);
        }

        return id;
    }

    public async Task<StoredCard?> GetAsync(string id, CancellationToken cancellation)
    {
        if (id.Length != 32 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return null;

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(Path.Combine(directory, id + ".card"), cancellation);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        return JsonSerializer.Deserialize<StoredCard>(protector.CreateProtector(id).Unprotect(bytes));
    }
}

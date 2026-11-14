using System.Net;
using System.Net.Http.Headers;

internal sealed class VercelBlobCardStorage(HttpClient client, VercelBlobSettings settings) : ICardStorage
{
    // Match the Vercel Blob SDK's server-side PUT protocol.
    private const string WriteEndpoint = "https://vercel.com/api/blob/";

    public async Task WriteAsync(string id, byte[] bytes, CancellationToken cancellation)
    {
        var pathname = "cards/" + id + ".card";
        using var request = new HttpRequestMessage(HttpMethod.Put, WriteEndpoint + "?pathname=" + Uri.EscapeDataString(pathname));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.Add("x-api-version", "13");
        request.Headers.Add("x-api-blob-request-id", $"{settings.StoreId}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}:{Guid.NewGuid():N}");
        request.Headers.Add("x-api-blob-request-attempt", "0");
        request.Headers.Add("x-vercel-blob-store-id", settings.StoreId);
        request.Headers.Add("x-vercel-blob-access", "private");
        request.Headers.Add("x-add-random-suffix", "0");
        request.Headers.Add("x-allow-overwrite", "0");
        request.Headers.Add("x-content-type", "application/octet-stream");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
    }

    public async Task<byte[]?> ReadAsync(string id, CancellationToken cancellation)
    {
        var address = $"https://{settings.StoreId}.private.blob.vercel-storage.com/cards/{id}.card";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);

        using var response = await client.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellation);
    }
}

internal sealed class VercelBlobSettings
{
    public string Token { get; }
    public string StoreId { get; }

    public VercelBlobSettings(IConfiguration configuration)
    {
        Token = configuration["BLOB_READ_WRITE_TOKEN"]?.Trim() ?? "";
        var parts = Token.Split('_', 5);
        if (!Token.StartsWith("vercel_blob_rw_", StringComparison.Ordinal) ||
            parts.Length != 5 || parts[3].Length is < 1 or > 63 || !parts[3].All(char.IsAsciiLetterOrDigit) ||
            parts[4].Length == 0)
            throw new InvalidOperationException("Set BLOB_READ_WRITE_TOKEN to a Vercel private Blob store read-write token.");

        StoreId = parts[3];
    }
}

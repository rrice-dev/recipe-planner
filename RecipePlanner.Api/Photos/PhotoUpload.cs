using RecipePlanner.Api.Ai;

namespace RecipePlanner.Api.Photos;

// Reads an uploaded photo sent as raw bytes (Content-Type: image/jpeg, png or webp).
// Shared by fridge scans and recipe photos so both get exactly the same checks.
//
// Raw bytes rather than a form: browsers can't send these content types cross-site without a
// CORS preflight, which this API never approves, so other sites can't make a logged-in
// user's browser upload anything.
public static class PhotoUpload
{
    public static async Task<(byte[]? Bytes, IResult? Error)> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        var contentType = request.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            return (null, Results.Problem("Send the photo as image/jpeg, image/png or image/webp.",
                statusCode: StatusCodes.Status415UnsupportedMediaType));

        if (request.ContentLength > PhotoProcessor.MaxUploadBytes)
            return (null, TooLarge());

        // Read at most one byte over the limit, even if Content-Length lied
        var bytes = await ReadUpToAsync(request.Body, PhotoProcessor.MaxUploadBytes + 1, ct);
        return bytes.Length > PhotoProcessor.MaxUploadBytes ? (null, TooLarge()) : (bytes, null);
    }

    private static IResult TooLarge() =>
        Results.Problem("The photo is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);

    private static async Task<byte[]> ReadUpToAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < maxBytes &&
               (read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxBytes - buffer.Length)), ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}

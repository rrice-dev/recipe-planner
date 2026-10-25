using SkiaSharp;

namespace RecipePlanner.Api.Ai;

// Turns an uploaded photo into a small, clean JPEG that is safe to send to the AI.
// Nothing is saved to disk or the database: the photo only lives in memory for this request.
public static class PhotoProcessor
{
    public const int MaxUploadBytes = 5 * 1024 * 1024;   // The page shrinks photos first, so real uploads are far smaller
    public const int MaxSourcePixels = 40_000_000;        // Refuse "decompression bombs": tiny files that expand to huge images
    public const int MaxOutputEdge = 1024;                // Image size is what you pay for, so keep it small
    private const int JpegQuality = 80;

    public static PhotoResult Process(byte[] bytes)
    {
        if (bytes.Length == 0) return PhotoResult.Fail("The photo was empty.");
        if (bytes.Length > MaxUploadBytes) return PhotoResult.Fail("The photo is too large.");

        // Check what the file really is from its first bytes, not from its name or Content-Type,
        // both of which the client controls.
        if (!LooksLikeSupportedImage(bytes)) return PhotoResult.Fail("Only JPEG, PNG or WebP photos are supported.");

        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        if (codec is null) return PhotoResult.Fail("The photo couldn't be read.");

        // Read the dimensions from the header BEFORE decoding the pixels
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxSourcePixels)
            return PhotoResult.Fail("The photo's dimensions are too large.");

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return PhotoResult.Fail("The photo couldn't be read.");

        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var resized = Shrink(upright);

        // Re-encoding from raw pixels writes a brand-new file. Nothing from the original survives:
        // no EXIF data (including the GPS location phones add), and no hidden or malformed bytes.
        using var image = SKImage.FromBitmap(resized);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return PhotoResult.Ok(jpeg.ToArray(), resized.Width, resized.Height);
    }

    private static bool LooksLikeSupportedImage(byte[] b) =>
        StartsWith(b, [0xFF, 0xD8, 0xFF]) ||                                       // JPEG
        StartsWith(b, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ||       // PNG
        (b.Length >= 12 && StartsWith(b, "RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8)); // WebP

    private static bool StartsWith(byte[] b, ReadOnlySpan<byte> prefix) =>
        b.Length >= prefix.Length && b.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    // Phones often store photos sideways plus a "rotate me" flag. Since we drop all metadata,
    // the rotation has to be applied to the pixels first or the AI would see a sideways fridge.
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                          or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        float w = source.Width, h = source.Height;

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-w, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180); canvas.Translate(-w, -h); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -h); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return result;
    }

    private static SKBitmap Shrink(SKBitmap source)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        var scale = Math.Min(1.0, (double)MaxOutputEdge / longEdge);
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
               ?? throw new InvalidOperationException("Resize failed.");
    }
}

public record PhotoResult(bool Success, byte[] Jpeg, int Width, int Height, string? Error)
{
    public static PhotoResult Ok(byte[] jpeg, int width, int height) => new(true, jpeg, width, height, null);
    public static PhotoResult Fail(string error) => new(false, [], 0, 0, error);
}

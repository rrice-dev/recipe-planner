using System.Buffers.Binary;
using System.IO.Compression;
using SkiaSharp;

namespace RecipePlanner.Tests;

public static class TestImages
{
    // Left half red, right half blue, so tests can tell which way the image was turned
    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 2f, height, red);
        }
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }

    // Inserts an EXIF block with an orientation flag and a GPS location, like a phone camera does
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        void U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); tiff.AddRange(b); }
        void U32(int v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); tiff.AddRange(b); }

        tiff.AddRange("MM"u8.ToArray()); U16(42); U32(8);          // big-endian TIFF header, IFD0 at offset 8
        U16(2);                                                     // IFD0: 2 entries
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);      // Orientation
        U16(0x8825); U16(4); U32(1); U32(8 + 2 + 24 + 4);           // pointer to the GPS IFD
        U32(0);                                                     // no next IFD
        U16(1);                                                     // GPS IFD: 1 entry
        U16(0x0001); U16(2); U32(2); tiff.AddRange("N\0\0\0"u8.ToArray()); // GPSLatitudeRef = "N"
        U32(0);

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new List<byte> { 0xFF, 0xE1 };
        var len = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)(payload.Length + 2));
        segment.AddRange(len); segment.AddRange(payload);

        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray(); // right after the start marker
    }

    // A valid PNG header claiming enormous dimensions, without the pixel data to back it up
    public static byte[] PngHeaderOnly(int width, int height)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Chunk(string type, byte[] data)
        {
            var len = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var crc = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes.Concat(data).ToArray()));
            png.AddRange(len); png.AddRange(typeBytes); png.AddRange(data); png.AddRange(crc);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 2;                                   // 8-bit RGB
        Chunk("IHDR", ihdr);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest)) z.Write(new byte[16]);
        Chunk("IDAT", ms.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    public static bool ContainsExif(byte[] jpeg) =>
        jpeg.AsSpan().IndexOf("Exif\0\0"u8) >= 0;

    public static SKColor ColorAt(byte[] image, float xFraction, float yFraction)
    {
        using var bitmap = SKBitmap.Decode(image);
        return bitmap.GetPixel((int)(bitmap.Width * xFraction), (int)(bitmap.Height * yFraction));
    }

    public static (int Width, int Height) Size(byte[] image)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(image));
        return (codec.Info.Width, codec.Info.Height);
    }

    public static bool IsRed(SKColor c) => c.Red > 180 && c.Blue < 80;
    public static bool IsBlue(SKColor c) => c.Blue > 180 && c.Red < 80;
}

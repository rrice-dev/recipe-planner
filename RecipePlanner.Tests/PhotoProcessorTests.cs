using SkiaSharp;
using RecipePlanner.Api.Ai;
using static RecipePlanner.Tests.TestImages;

namespace RecipePlanner.Tests;

public class PhotoProcessorTests
{
    [Fact]
    public void Removes_exif_including_gps_location()
    {
        var withGps = WithExif(Jpeg(200, 100), orientation: 1);
        Assert.True(ContainsExif(withGps));                  // sanity check: the test image really has EXIF

        var result = PhotoProcessor.Process(withGps);

        Assert.True(result.Success);
        Assert.False(ContainsExif(result.Jpeg));
    }

    [Fact]
    public void Shrinks_large_photos_and_keeps_the_aspect_ratio()
    {
        var result = PhotoProcessor.Process(Jpeg(3000, 1500));
        Assert.Equal((1024, 512), Size(result.Jpeg));
    }

    [Fact]
    public void Leaves_small_photos_at_their_size()
    {
        var result = PhotoProcessor.Process(Jpeg(400, 300));
        Assert.Equal((400, 300), Size(result.Jpeg));
    }

    // The source has red on the left and blue on the right. After applying the phone's
    // rotation flag, the red half should end up where a person viewing the photo would see it.
    [Theory]
    [InlineData((ushort)1, false, "left")]
    [InlineData((ushort)3, false, "right")]    // upside down
    [InlineData((ushort)6, true, "top")]       // rotate 90° clockwise (most common for phones)
    [InlineData((ushort)8, true, "bottom")]    // rotate 90° counter-clockwise
    [InlineData((ushort)7, true, "bottom")]    // transverse
    public void Applies_the_rotation_flag_to_the_pixels(ushort orientation, bool swapsSides, string redSide)
    {
        var result = PhotoProcessor.Process(WithExif(Jpeg(200, 100), orientation));

        Assert.True(result.Success);
        Assert.Equal(swapsSides ? (100, 200) : (200, 100), Size(result.Jpeg));
        var (x, y) = redSide switch
        {
            "left" => (0.1f, 0.5f), "right" => (0.9f, 0.5f),
            "top" => (0.5f, 0.1f), _ => (0.5f, 0.9f)
        };
        Assert.True(IsRed(ColorAt(result.Jpeg, x, y)));
        Assert.True(IsBlue(ColorAt(result.Jpeg, 1 - x, 1 - y)));
    }

    // Big photos decode at a fraction of their size to save memory; the result must look the same
    [Theory]
    [InlineData((ushort)1, false, "left")]
    [InlineData((ushort)6, true, "top")]
    [InlineData((ushort)8, true, "bottom")]
    public void Large_photos_come_out_the_same_after_a_reduced_decode(ushort orientation, bool swapsSides, string redSide)
    {
        var result = PhotoProcessor.Process(WithExif(Jpeg(4000, 2000), orientation));

        Assert.True(result.Success);
        Assert.Equal(swapsSides ? (512, 1024) : (1024, 512), Size(result.Jpeg));
        var (x, y) = redSide switch { "left" => (0.1f, 0.5f), "top" => (0.5f, 0.1f), _ => (0.5f, 0.9f) };
        Assert.True(IsRed(ColorAt(result.Jpeg, x, y)));
        Assert.True(IsBlue(ColorAt(result.Jpeg, 1 - x, 1 - y)));
    }

    [Fact]
    public void Large_jpegs_are_decoded_at_a_reduced_size_but_never_below_the_target()
    {
        using var codec = SKCodec.Create(new SKMemoryStream(Jpeg(4000, 2000)));
        var info = PhotoProcessor.DecodeInfo(codec, 1024);
        Assert.True(info.Width < 4000, $"decoded at full width {info.Width}");
        Assert.True(info.Width >= 1024);

        using var small = SKCodec.Create(new SKMemoryStream(Jpeg(800, 600)));
        Assert.Equal((800, 600), (PhotoProcessor.DecodeInfo(small, 1024).Width, PhotoProcessor.DecodeInfo(small, 1024).Height));
    }

    [Fact]
    public void Rejects_files_that_are_not_images_even_if_named_like_one()
    {
        var result = PhotoProcessor.Process("<?php system($_GET['cmd']); ?>"u8.ToArray());
        Assert.False(result.Success);
    }

    [Fact]
    public void Rejects_a_fake_jpeg_header_followed_by_garbage()
    {
        var fake = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(Enumerable.Repeat((byte)0x41, 500)).ToArray();
        Assert.False(PhotoProcessor.Process(fake).Success);
    }

    [Fact]
    public void Rejects_decompression_bombs_before_decoding_pixels()
    {
        // 20,000 x 20,000 = 400 million pixels (about 1.6 GB of memory) from a file under 100 bytes
        var bomb = PngHeaderOnly(20_000, 20_000);
        Assert.True(bomb.Length < 200);

        var result = PhotoProcessor.Process(bomb);

        Assert.False(result.Success);
        Assert.Contains("dimensions", result.Error);
    }

    [Fact]
    public void Rejects_empty_and_oversized_uploads()
    {
        Assert.False(PhotoProcessor.Process([]).Success);
        Assert.False(PhotoProcessor.Process(new byte[PhotoProcessor.MaxUploadBytes + 1]).Success);
    }
}

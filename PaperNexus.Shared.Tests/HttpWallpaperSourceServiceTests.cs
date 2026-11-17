using Newtonsoft.Json;
using PaperNexus.Core;
using Xunit;

namespace PaperNexus.Tests;

public class HttpWallpaperSourceServiceTests
{
    private static WallpaperSource MakeSource(string imageJPath = "$.images[*].url", string titleJPath = "$.images[*].title") => new()
    {
        Name = "TestSource",
        Url = "https://example.com/api",
        ImageUrlJPath = imageJPath,
        TitleJPath = titleJPath,
        IsEnabled = true,
    };

    [Fact]
    public void ParseImages_ValidJsonAndJPath_ReturnsImages()
    {
        var json = """{"images":[{"url":"https://img/1.jpg","title":"Sunset"},{"url":"https://img/2.jpg","title":"Mountain"}]}""";
        var result = HttpWallpaperSourceService.ParseImages(MakeSource(), json);
        Assert.Equal(2, result.Count);
        Assert.Equal("https://img/1.jpg", result[0].ImageUrl);
        Assert.Equal("Sunset", result[0].Title);
        Assert.Equal("https://img/2.jpg", result[1].ImageUrl);
        Assert.Equal("Mountain", result[1].Title);
    }

    [Fact]
    public void ParseImages_EmptyJsonArray_ReturnsEmptyList()
    {
        var json = """{"images":[]}""";
        var result = HttpWallpaperSourceService.ParseImages(MakeSource(), json);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseImages_MalformedJson_ThrowsWithPreview()
    {
        var html = "<html><body>Not JSON</body></html>";
        var ex = Assert.Throws<JsonException>(() => HttpWallpaperSourceService.ParseImages(MakeSource(), html));
        Assert.Contains("TestSource", ex.Message);
        Assert.Contains("not valid JSON", ex.Message);
        Assert.Contains("<html>", ex.Message);
    }

    [Fact]
    public void ParseImages_TruncatedJson_ThrowsWithPreview()
    {
        var truncated = """{"images":[{"url":"https://img/1.jpg","title":""";
        var ex = Assert.Throws<JsonException>(() => HttpWallpaperSourceService.ParseImages(MakeSource(), truncated));
        Assert.Contains("TestSource", ex.Message);
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void ParseImages_InvalidImageUrlJPath_ThrowsIdentifyingField()
    {
        var json = """{"images":[{"url":"https://img/1.jpg","title":"Sunset"}]}""";
        var ex = Assert.Throws<JsonException>(() => HttpWallpaperSourceService.ParseImages(MakeSource(imageJPath: "$[*."), json));
        Assert.Contains("TestSource", ex.Message);
        Assert.Contains("ImageUrlJPath", ex.Message);
        Assert.Contains("$[*.", ex.Message);
    }

    [Fact]
    public void ParseImages_InvalidTitleJPath_ThrowsIdentifyingField()
    {
        var json = """{"images":[{"url":"https://img/1.jpg","title":"Sunset"}]}""";
        var ex = Assert.Throws<JsonException>(() => HttpWallpaperSourceService.ParseImages(MakeSource(titleJPath: "$[*."), json));
        Assert.Contains("TestSource", ex.Message);
        Assert.Contains("TitleJPath", ex.Message);
        Assert.Contains("$[*.", ex.Message);
    }

    [Fact]
    public void ParseImages_JPathMatchesNothing_ReturnsEmptyList()
    {
        var json = """{"images":[{"url":"https://img/1.jpg","title":"Sunset"}]}""";
        var result = HttpWallpaperSourceService.ParseImages(MakeSource(imageJPath: "$.nonexistent[*].url", titleJPath: "$.nonexistent[*].title"), json);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseImages_MoreUrlsThanTitles_ZipsTruncates()
    {
        var json = """{"urls":["https://img/1.jpg","https://img/2.jpg"],"titles":["Only One"]}""";
        var result = HttpWallpaperSourceService.ParseImages(MakeSource(imageJPath: "$.urls[*]", titleJPath: "$.titles[*]"), json);
        Assert.Single(result);
        Assert.Equal("https://img/1.jpg", result[0].ImageUrl);
        Assert.Equal("Only One", result[0].Title);
    }
}

using Newtonsoft.Json.Linq;
using PaperNexus.Core;

namespace PaperNexus;

public class HttpWallpaperSourceService
{
    private readonly ILogger<HttpWallpaperSourceService> _logger;

    // A single long-lived HttpClient shared across all calls to avoid socket exhaustion.
    // HttpClient is thread-safe for concurrent requests; creating one per call drains the
    // ephemeral port pool because disposed clients leave sockets in TIME_WAIT for ~4 min.
    // The service is registered as a singleton, so this instance lives for the process lifetime.
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpWallpaperSourceService(ILogger<HttpWallpaperSourceService> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    // Fetches the JSON feed from the wallpaper source URL and returns all images
    // extracted via the source's configured JPath expressions.
    // The optional cancellationToken is forwarded to the HTTP call so callers
    // can cancel in-flight requests (e.g. when the Test dialog is closed mid-request).
    public async Task<List<WallpaperImage>> GetImagesAsync(WallpaperSource source, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting images from source '{Name}': {Url}", source.Name, source.Url);
        var watch = Stopwatch.StartNew();
        using var getResponse = await _client.GetAsync(source.Url, cancellationToken);
        if (!getResponse.IsSuccessStatusCode)
        {
            var msg = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)getResponse.StatusCode} {getResponse.StatusCode} from '{source.Url}': {msg}");
        }

        var json = await getResponse.Content.ReadAsStringAsync(cancellationToken);

        List<WallpaperImage> images;
        try
        {
            images = ParseImages(source, json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse images from source '{Name}'.", source.Name);
            throw;
        }

        _logger.LogInformation("Got {Count} image(s) from '{Name}' in {Elapsed}", images.Count, source.Name, watch.Elapsed);
        return images;
    }

    // Uses the source's JPath expressions to extract parallel lists of image URLs and titles
    // from the raw JSON, then zips them into WallpaperImage records.
    // Throws JsonException with a diagnostic message if the JSON is malformed or a JPath is invalid.
    internal static List<WallpaperImage> ParseImages(WallpaperSource source, string json)
    {
        // Parse the raw JSON response into a token tree
        JToken token;
        try
        {
            token = JToken.Parse(json);
        }
        catch (JsonReaderException ex)
        {
            var preview = json.Length > 200 ? json[..200] + "..." : json;
            throw new JsonException($"Source '{source.Name}': response is not valid JSON. Preview: {preview}", ex);
        }

        // Extract image URLs using the configured JPath expression
        List<string> imageUrls;
        try
        {
            imageUrls = token.SelectTokens(source.ImageUrlJPath)
                .Select(t => t.Value<string>() ?? string.Empty)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Source '{source.Name}': ImageUrlJPath '{source.ImageUrlJPath}' is invalid: {ex.Message}", ex);
        }

        // Extract titles using the configured JPath expression
        List<string> titles;
        try
        {
            titles = token.SelectTokens(source.TitleJPath)
                .Select(t => t.Value<string>() ?? string.Empty)
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Source '{source.Name}': TitleJPath '{source.TitleJPath}' is invalid: {ex.Message}", ex);
        }

        // Zip stops at the shorter list, so mismatched result counts are handled gracefully
        return imageUrls
            .Zip(titles, (url, title) => new WallpaperImage { ImageUrl = url, Title = title })
            .ToList();
    }
}

public class WallpaperImage
{
    public string ImageUrl { get; set; }
    public string Title { get; set; }
}

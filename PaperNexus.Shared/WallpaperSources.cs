using Cronos;

namespace PaperNexus;

// The wallpaper sources: the feeds the downloader reads. Every screen on either device lists,
// adds, edits, deletes, turns on and off, and tests sources through this one service, so the
// rules for a source are written once. Downloading from them belongs to IDownloadWallpapers.
//
// Names are unique, compared ignoring case after trimming, which is also how the downloader
// records each source's last download time. A settings file written before this rule may still
// hold two sources with the same name; it loads and lists as it is. So a source is known by its
// SourceKey: its name plus which of the sources sharing that name it is (0 for the first). An
// edit, a delete or the on/off switch acts on exactly the source its key names, never on a twin.
public interface IWallpaperSources
{
    // Raised after every change this service saved, so each open screen can show it. Raised on
    // a background thread.
    public event Action? Changed;

    // Every source with its key, in its stored order.
    public Task<IReadOnlyList<ListedSource>> ListAsync();

    // Checks the fields and adds the source at the end. Returns the first problem found, in the
    // order a person reads the form, and saves nothing then; null when it was saved.
    public Task<SourceProblem?> AddAsync(SourceFields fields);

    // Checks the fields and replaces the source the key names, in the same place. Keeping its own
    // name is never a clash, even for one of two older same-named sources; taking another
    // source's name is. A source that keeps its name keeps its last download time; a renamed one
    // counts as never downloaded. When the source is gone (deleted meanwhile), it is added at the
    // end, unless its name is taken. Returns the first problem, or null when it was saved.
    public Task<SourceProblem?> ReplaceAsync(SourceKey key, SourceFields fields);

    // Deletes the source the key names. Nothing happens when there is none.
    public Task DeleteAsync(SourceKey key);

    // Turns the source the key names on or off. Nothing happens when there is none.
    public Task SetEnabledAsync(SourceKey key, bool isEnabled);

    // Reads the source's feed with its picture-finding rules, without saving anything. The
    // address and both rules are checked first, as Save checks them (the name may be empty). A
    // feed that cannot be read or understood throws to the caller; cancelling throws
    // OperationCanceledException.
    public Task<SourceTestOutcome> TestAsync(SourceFields fields, CancellationToken cancellationToken);
}

// The six things a person sets on a source, as typed.
public sealed record SourceFields(
    string Name,
    string Url,
    string ImageUrlJPath,
    string TitleJPath,
    string CronExpression,
    bool IsEnabled);

// Which source: its name, and which of the sources sharing that name (ignoring case, trimmed) it
// is, 0 for the first. Only an older settings file can hold more than one.
public sealed record SourceKey(string Name, int Occurrence)
{
    // Whether both keys name the same source.
    public bool IsSameSource(SourceKey other)
    {
        other.ThrowIfNull();
        var sameName = string.Equals(Name?.Trim(), other.Name?.Trim(), StringComparison.OrdinalIgnoreCase);
        return sameName && Occurrence == other.Occurrence;
    }
}

// A listed source: the key that names it and its fields.
public sealed record ListedSource(SourceKey Key, SourceFields Fields);

// Why a source was not saved or tested.
public enum SourceProblem
{
    NameRequired,
    UrlRequired,
    UrlNotHttps,
    ImageUrlJPathRequired,
    TitleJPathRequired,
    InvalidCron,
    NameTaken,
}

// What a test found: the problem that stopped it before reading the feed, or the pictures the
// feed lists.
public sealed record SourceTestOutcome(SourceProblem? Problem, IReadOnlyList<WallpaperImage> Images);

internal sealed class WallpaperSources : IWallpaperSources, IAddSingleton<IWallpaperSources>
{
    // The schedule a source gets when its schedule is left blank: every hour.
    internal const string DefaultCronExpression = "0 * * * *";

    private readonly ISettingsStore _settingsStore;
    private readonly HttpWallpaperSourceService _feedReader;

    public event Action? Changed;

    public WallpaperSources(ISettingsStore settingsStore, HttpWallpaperSourceService feedReader)
    {
        _settingsStore = settingsStore.ThrowIfNull();
        _feedReader = feedReader.ThrowIfNull();
    }

    public async Task<IReadOnlyList<ListedSource>> ListAsync()
    {
        var settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        var listed = new List<ListedSource>();
        for (var index = 0; index < settings.Sources.Count; index++)
        {
            var source = settings.Sources[index];
            var occurrence = settings.Sources.Take(index).Count(s => SameName(s.Name, source.Name));
            var key = new SourceKey(Clean(source.Name), occurrence);
            listed.Add(new ListedSource(key, ToFields(source)));
        }
        return listed;
    }

    public async Task<SourceProblem?> AddAsync(SourceFields fields)
    {
        var problem = Check(fields, out var source);
        if (problem is not null)
            return problem;

        try
        {
            await _settingsStore.UpdateAsync(latest =>
            {
                if (FindIndex(latest.Sources, source.Name) >= 0)
                    throw new RefusedException(SourceProblem.NameTaken);
                latest.Sources.Add(source);
            }).ConfigureAwait(false);
        }
        catch (RefusedException refused)
        {
            return refused.Problem;
        }

        Raise();
        return null;
    }

    public async Task<SourceProblem?> ReplaceAsync(SourceKey key, SourceFields fields)
    {
        key.ThrowIfNull();
        var problem = Check(fields, out var source);
        if (problem is not null)
            return problem;

        // Keeping its own name (even with other capitals) is never a clash, so either of two
        // older same-named sources can be edited without renaming it.
        var keepsName = SameName(key.Name, source.Name);
        try
        {
            await _settingsStore.UpdateAsync(latest =>
            {
                var index = FindIndex(latest.Sources, key);
                var nameTaken = FindIndex(latest.Sources, source.Name) >= 0;
                if (index < 0)
                {
                    // Deleted while the form was open: the edited source is added at the end,
                    // as the desktop always did, unless that would repeat a name
                    if (nameTaken)
                        throw new RefusedException(SourceProblem.NameTaken);
                    latest.Sources.Add(source);
                    return;
                }

                if (!keepsName && nameTaken)
                    throw new RefusedException(SourceProblem.NameTaken);

                if (keepsName)
                    source.LastDownloadUtc = latest.Sources[index].LastDownloadUtc;
                latest.Sources[index] = source;
            }).ConfigureAwait(false);
        }
        catch (RefusedException refused)
        {
            return refused.Problem;
        }

        Raise();
        return null;
    }

    public async Task DeleteAsync(SourceKey key)
    {
        key.ThrowIfNull();
        await _settingsStore.UpdateAsync(latest =>
        {
            var index = FindIndex(latest.Sources, key);
            if (index >= 0)
                latest.Sources.RemoveAt(index);
        }).ConfigureAwait(false);
        Raise();
    }

    public async Task SetEnabledAsync(SourceKey key, bool isEnabled)
    {
        key.ThrowIfNull();
        await _settingsStore.UpdateAsync(latest =>
        {
            var index = FindIndex(latest.Sources, key);
            if (index >= 0)
                latest.Sources[index].IsEnabled = isEnabled;
        }).ConfigureAwait(false);
        Raise();
    }

    public async Task<SourceTestOutcome> TestAsync(SourceFields fields, CancellationToken cancellationToken)
    {
        fields.ThrowIfNull();
        var url = Clean(fields.Url);
        var imageUrlJPath = Clean(fields.ImageUrlJPath);
        var titleJPath = Clean(fields.TitleJPath);
        var problem = CheckFeed(url, imageUrlJPath, titleJPath);
        if (problem is not null)
            return new SourceTestOutcome(problem, []);

        var source = new WallpaperSource
        {
            Name = Clean(fields.Name),
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
        };
        var images = await _feedReader.GetImagesAsync(source, cancellationToken).ConfigureAwait(false);
        return new SourceTestOutcome(null, images);
    }

    // The rules a source must meet before it is saved, in the order a person reads the form.
    // A blank schedule becomes every hour; a schedule must be a 5-field cron expression. The
    // name's uniqueness is checked against the latest list, inside the save.
    private static SourceProblem? Check(SourceFields fields, out WallpaperSource? source)
    {
        fields.ThrowIfNull();
        source = null;
        var name = Clean(fields.Name);
        if (name.Length == 0)
            return SourceProblem.NameRequired;

        var url = Clean(fields.Url);
        var imageUrlJPath = Clean(fields.ImageUrlJPath);
        var titleJPath = Clean(fields.TitleJPath);
        var feedProblem = CheckFeed(url, imageUrlJPath, titleJPath);
        if (feedProblem is not null)
            return feedProblem;

        var cron = Clean(fields.CronExpression);
        if (cron.Length == 0)
            cron = DefaultCronExpression;
        try
        {
            CronExpression.Parse(cron);
        }
        catch (CronFormatException)
        {
            return SourceProblem.InvalidCron;
        }

        source = new WallpaperSource
        {
            Name = name,
            Type = WallpaperSourceType.HttpJson,
            Url = url,
            ImageUrlJPath = imageUrlJPath,
            TitleJPath = titleJPath,
            CronExpression = cron,
            IsEnabled = fields.IsEnabled,
        };
        return null;
    }

    // The address and the two picture-finding rules, which both Save and Test need.
    private static SourceProblem? CheckFeed(string url, string imageUrlJPath, string titleJPath)
    {
        if (url.Length == 0)
            return SourceProblem.UrlRequired;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            return SourceProblem.UrlNotHttps;
        if (imageUrlJPath.Length == 0)
            return SourceProblem.ImageUrlJPathRequired;
        if (titleJPath.Length == 0)
            return SourceProblem.TitleJPathRequired;
        return null;
    }

    private static string Clean(string? text) => text?.Trim() ?? string.Empty;

    // The first source with this name, ignoring case and surrounding spaces, or -1: whether a
    // name is taken.
    private static int FindIndex(List<WallpaperSource> sources, string name)
    {
        var index = sources.FindIndex(s => SameName(s.Name, name));
        return index;
    }

    // The source the key names: the key's occurrence among the sources of its name, or -1.
    private static int FindIndex(List<WallpaperSource> sources, SourceKey key)
    {
        var seen = 0;
        for (var index = 0; index < sources.Count; index++)
        {
            if (!SameName(sources[index].Name, key.Name))
                continue;
            if (seen == key.Occurrence)
                return index;
            seen++;
        }
        return -1;
    }

    private static bool SameName(string? first, string? second)
    {
        var a = Clean(first);
        var b = Clean(second);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static SourceFields ToFields(WallpaperSource source)
    {
        var fields = new SourceFields(
            source.Name ?? string.Empty,
            source.Url ?? string.Empty,
            source.ImageUrlJPath ?? string.Empty,
            source.TitleJPath ?? string.Empty,
            source.CronExpression ?? string.Empty,
            source.IsEnabled);
        return fields;
    }

    private void Raise() => Changed?.Invoke();

    // Thrown inside a settings update to refuse it, so nothing is written.
    private sealed class RefusedException(SourceProblem problem) : Exception
    {
        public SourceProblem Problem { get; } = problem;
    }
}

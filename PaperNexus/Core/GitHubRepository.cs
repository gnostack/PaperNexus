using System.Reflection;

namespace PaperNexus.Core;

// The GitHub repository this build was produced from: the one place the app learns its own
// owner/name. The value is not written in the source; the build embeds the checkout's git
// origin URL as AssemblyMetadata("RepositoryUrl") (see the RequireRepositoryUrl target in
// PaperNexus.csproj), so a repository transfer or rename reaches clients with the next release.
internal sealed record GitHubRepository(string Owner, string Name)
{
    internal const string MetadataKey = "RepositoryUrl";

    // Lazy rather than a static initializer so a missing value surfaces as the clear
    // InvalidOperationException from Parse, not a TypeInitializationException.
    private static readonly Lazy<GitHubRepository> _current = new(() => FromAssembly(typeof(GitHubRepository).Assembly));

    // The repository embedded in the running PaperNexus assembly.
    public static GitHubRepository Current => _current.Value;

    // "owner/name", the form the GitHub REST API takes after /repos/.
    public string Slug => $"{Owner}/{Name}";

    // The repository's home page on github.com.
    public string WebUrl => $"https://github.com/{Slug}";

    // Reads the RepositoryUrl metadata from the given assembly and parses it.
    internal static GitHubRepository FromAssembly(Assembly assembly)
    {
        var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var repositoryUrl = attributes.FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        return Parse(repositoryUrl);
    }

    // Parses a git remote URL into owner/name. Accepts the forms a GitHub origin takes:
    //   https://github.com/owner/name(.git)   ssh://git@github.com/owner/name(.git)
    //   git@github.com:owner/name(.git)
    // Anything else - missing, not on github.com, or not exactly owner/name - is refused,
    // because every consumer builds a github.com address from the result and a guess
    // would send update checks and bug reports to the wrong place.
    internal static GitHubRepository Parse(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            throw new InvalidOperationException(
                $"This build has no {MetadataKey} assembly metadata, so the GitHub repository is unknown.");
        }

        var trimmed = repositoryUrl.Trim();
        string host;
        string path;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "ssh"))
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else
        {
            // scp-like syntax: [user@]host:owner/name
            var colon = trimmed.IndexOf(':');
            if (colon <= 0 || trimmed.Contains("://"))
                throw Malformed(repositoryUrl);
            var userAndHost = trimmed[..colon];
            var at = userAndHost.LastIndexOf('@');
            host = at >= 0 ? userAndHost[(at + 1)..] : userAndHost;
            path = trimmed[(colon + 1)..];
        }

        if (!string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw Malformed(repositoryUrl);

        var segments = path.Trim('/').Split('/');
        if (segments.Length != 2)
            throw Malformed(repositoryUrl);

        var owner = segments[0];
        var name = segments[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^".git".Length];
        if (owner.Length == 0 || name.Length == 0)
            throw Malformed(repositoryUrl);

        return new GitHubRepository(owner, name);
    }

    private static InvalidOperationException Malformed(string repositoryUrl)
    {
        return new InvalidOperationException(
            $"The {MetadataKey} assembly metadata '{repositoryUrl}' is not a github.com owner/name repository URL.");
    }
}

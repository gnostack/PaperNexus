namespace PaperNexus.Core;

// The version label both devices show: "v" and the major version, e.g. "v12". The desktop takes
// the number from its assembly version, which CI sets to the build number; the phone from its
// package's version name.
public static class AppVersionLabel
{
    // "v" and the first number of a version name such as "12.0.0"; "v0" when it has none.
    public static string FromVersionName(string? versionName)
    {
        var first = (versionName ?? string.Empty).Trim().Split('.', 2)[0];
        var label = int.TryParse(first, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var major)
            ? $"v{major}"
            : "v0";
        return label;
    }
}

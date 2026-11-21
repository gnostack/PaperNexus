using Android.Content.PM;
using PaperNexus.Core;

namespace PaperNexus;

// The phone's facts about the running app. The version is the package's version name, shown as
// "v" and its major number, as the desktop shows its build number: the Android package workflow
// builds version name N.0.0 from the desktop release's build number N, so both show "vN"; a local
// build is 0.0.0 and shows "v0" (PaperNexus.Android.csproj). The phone has no --debug start:
// debug mode is the setting alone.
internal sealed class AndroidAppInfo : IAppInfo, IAddSingleton<IAppInfo>
{
    public AndroidAppInfo()
    {
        var context = global::Android.App.Application.Context;
        var packageManager = context.PackageManager!;
        var packageName = context.PackageName!;
        var package = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? packageManager.GetPackageInfo(packageName, PackageManager.PackageInfoFlags.Of(0L))
            : GetPackageInfoBefore33(packageManager, packageName);
        Version = AppVersionLabel.FromVersionName(package?.VersionName);
    }

    public string Version { get; }

    public bool StartedInDebugMode => false;

    // The overload Android 12 has; replaced by the flags overload in Android 13.
#pragma warning disable CA1422
    private static PackageInfo? GetPackageInfoBefore33(PackageManager packageManager, string packageName) =>
        packageManager.GetPackageInfo(packageName, 0);
#pragma warning restore CA1422
}

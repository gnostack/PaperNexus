using Android.App;
using Android.App.Job;
using Microsoft.Extensions.DependencyInjection;

namespace PaperNexus;

// The scheduled download run (AndroidBackgroundScheduler): Android starts the app's process if
// it is not running and binds this once the run's time has come and a network is there. The
// shared BackgroundWork downloads the due sources on a background thread, records what failed,
// finishes the job and arms the next run. A run Android stops part way is not rescheduled here
// (no hidden retry): the next plan arms the next run, and partial pictures are removed by the
// next run (DownloadWallpapers).
[Service(Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public sealed class DownloadJobService : JobService
{
    // Held until the job is finished, so Android never reads it as abandoned
    private JobParameters? _running;

    public override bool OnStartJob(JobParameters? @params)
    {
        _running = @params;
        var application = (MainApplication)ApplicationContext!;
        var work = application.Services.GetRequiredService<BackgroundWork>();
        _ = Task.Run(() => work.RunDownloadsAsync(() =>
        {
            JobFinished(@params, false);
            _running = null;
        }));
        return true;
    }

    public override bool OnStopJob(JobParameters? @params) => false;
}

using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace SteelSeriesDiscordStreamFix
{
    internal static class AppUpdater
    {
        private const string GithubRepoUrl = "https://github.com/0slvr/SteelSeriesDiscordStreamFix";

        public static async Task CheckAndApplyAsync(string[] restartArgs = null)
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Logger.Log("AppUpdater: no update available.");
                    return;
                }

                Logger.Log($"AppUpdater: update found -> {newVersion.TargetFullRelease.Version}");
                await mgr.DownloadUpdatesAsync(newVersion);

                mgr.ApplyUpdatesAndRestart(newVersion, restartArgs);
            }
            catch (NotInstalledException)
            {
                Logger.Log("AppUpdater: running from source/dev (not installed via Velopack), skipping update check.");
            }
            catch (Exception ex)
            {
                Logger.Log("AppUpdater failed: " + ex);
            }
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesInteractiveAsync(Action<string> statusCallback, string[] restartArgs = null)
        {
            try
            {
                statusCallback?.Invoke("Checking for updates...");

                var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    Logger.Log("AppUpdater (manual): no update available.");
                    return UpdateCheckResult.UpToDate;
                }

                Logger.Log($"AppUpdater (manual): update found -> {newVersion.TargetFullRelease.Version}");
                statusCallback?.Invoke($"Downloading update {newVersion.TargetFullRelease.Version}...");
                await mgr.DownloadUpdatesAsync(newVersion);

                statusCallback?.Invoke("Restarting to apply update...");
                mgr.ApplyUpdatesAndRestart(newVersion, restartArgs);
                return UpdateCheckResult.Updating;
            }
            catch (NotInstalledException)
            {
                Logger.Log("AppUpdater (manual): running from source/dev, cannot check for updates.");
                return UpdateCheckResult.NotInstalled;
            }
            catch (Exception ex)
            {
                Logger.Log("AppUpdater (manual) failed: " + ex);
                return UpdateCheckResult.Error;
            }
        }
    }

    internal enum UpdateCheckResult
    {
        UpToDate,
        Updating,
        NotInstalled,
        Error
    }
}
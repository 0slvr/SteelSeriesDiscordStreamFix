using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace SteelSeriesDiscordStreamFix
{
    internal static class AppUpdater
    {
        // Update files (full/delta/releases.win.json) live in this separate repo.
        // Users download Setup.exe from the main repo; the app itself reads updates from here.
        private const string GithubRepoUrl = "https://github.com/0slvr/SteelSeriesDiscordStreamFix-updates";

        /// <summary>
        /// Version of an update that was downloaded in the background and will be
        /// installed the next time the app exits. Null when nothing is pending.
        /// </summary>
        public static string PendingVersion { get; private set; }

        /// <summary>
        /// Background check at startup: downloads a new version if there is one, but does NOT
        /// restart the app. The update is installed silently when the app closes
        /// (for example at logoff/shutdown), so it never interrupts a game or a call.
        /// </summary>
        public static async Task CheckAndStageAsync(string[] restartArgs = null, bool applyNowIfAutoStart = false)
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

                PendingVersion = newVersion.TargetFullRelease.Version.ToString();

                // This app almost never exits on its own, so waiting for exit could delay updates for weeks.
                // At Windows login (--autostart) nobody is in a game or call yet, so apply and restart right away.
                if (applyNowIfAutoStart)
                {
                    Logger.Log($"AppUpdater: update {PendingVersion} downloaded at startup — applying now.");
                    mgr.ApplyUpdatesAndRestart(newVersion.TargetFullRelease, restartArgs);
                    return;
                }

                // Otherwise install when the app exits (Exit button, logoff or shutdown).
                mgr.WaitExitThenApplyUpdates(newVersion.TargetFullRelease, true, false, restartArgs);
                Logger.Log($"AppUpdater: update {PendingVersion} downloaded; it will be installed when the app closes.");
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
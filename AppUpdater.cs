using System;
using System.Threading;
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

        // The app almost never exits on its own, so a single check at startup would leave long-running
        // installs on an old version for weeks. Re-check regularly; retry sooner after a failure
        // (typically no network yet right after Windows login).
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

        // Background and manual checks must never run at the same time.
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private static volatile bool _notInstalled;
        private static volatile bool _lastCheckFailed;

        /// <summary>
        /// Version of an update that was downloaded in the background and will be
        /// installed the next time the app exits. Null when nothing is pending.
        /// </summary>
        public static string PendingVersion { get; private set; }

        /// <summary>
        /// Set by the app: closes the app through its NORMAL exit path (on the UI thread), so cleanup runs
        /// first (Sonar devices restored, Discord un-muted). Used to apply an update that is waiting.
        /// </summary>
        public static Action RequestExit { get; set; }

        /// <summary>
        /// Checks for updates now and then every few hours until one has been downloaded.
        /// Never throws.
        /// </summary>
        public static async Task RunPeriodicChecksAsync(Func<bool> isExiting, string[] restartArgs = null, bool applyNowIfAutoStart = false)
        {
            bool first = true;
            while (!isExiting())
            {
                await CheckAndStageAsync(restartArgs, first && applyNowIfAutoStart);
                first = false;

                // Running from source, or an update is already waiting to be installed at exit.
                if (_notInstalled || PendingVersion != null) return;

                try { await Task.Delay(_lastCheckFailed ? RetryAfterFailure : CheckInterval); }
                catch { return; }
            }
        }

        /// <summary>
        /// Background check: downloads a new version if there is one, but does NOT restart the app
        /// immediately. The update is installed silently when the app closes (for example at
        /// logoff/shutdown), so it never interrupts a game or a call.
        /// </summary>
        public static async Task CheckAndStageAsync(string[] restartArgs = null, bool applyNowIfAutoStart = false)
        {
            await Gate.WaitAsync();
            try
            {
                if (PendingVersion != null) return; // already downloaded and waiting

                _lastCheckFailed = false;
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

                // At Windows login (--autostart) nobody is in a game or call yet, so apply and restart right away.
                if (applyNowIfAutoStart)
                {
                    Logger.Log($"AppUpdater: update {PendingVersion} downloaded at startup — applying now.");
                    ApplyAfterNormalExit(mgr, newVersion, restartArgs);
                    return;
                }

                // Otherwise install when the app exits (Exit button, logoff or shutdown).
                mgr.WaitExitThenApplyUpdates(newVersion.TargetFullRelease, true, false, restartArgs);
                Logger.Log($"AppUpdater: update {PendingVersion} downloaded; it will be installed when the app closes.");
            }
            catch (NotInstalledException)
            {
                _notInstalled = true;
                Logger.Log("AppUpdater: running from source/dev (not installed via Velopack), skipping update check.");
            }
            catch (Exception ex)
            {
                _lastCheckFailed = true;
                Logger.Log("AppUpdater failed: " + ex);
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesInteractiveAsync(Action<string> statusCallback, string[] restartArgs = null)
        {
            await Gate.WaitAsync();
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
                ApplyAfterNormalExit(mgr, newVersion, restartArgs);
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
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Installs the downloaded update and restarts the app. The app is closed through its normal exit path
        /// (so Sonar devices are restored and Discord is un-muted first), then the new version is started.
        /// If that fails the app simply keeps running, still fully working.
        /// </summary>
        private static void ApplyAfterNormalExit(UpdateManager mgr, UpdateInfo info, string[] restartArgs)
        {
            var exit = RequestExit;
            if (exit == null)
            {
                // No exit hook registered (should not happen): fall back to Velopack's direct restart.
                mgr.ApplyUpdatesAndRestart(info.TargetFullRelease, restartArgs);
                return;
            }

            // silent = true, restart = true: wait for this process to exit, install, start the new version.
            mgr.WaitExitThenApplyUpdates(info.TargetFullRelease, true, true, restartArgs);
            exit();
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

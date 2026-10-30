using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Update checks are informational only: the app NEVER downloads or installs anything by itself.
    /// A background check finds a new version and raises <see cref="UpdateFound"/> (the app shows a Windows
    /// notification); the user then chooses Update now / Skip this version / Later. The same choice is
    /// available in the Settings window.
    /// </summary>
    internal static class AppUpdater
    {
        // Update files (full/delta/releases.win.json) live in this separate repo.
        // Users download Setup.exe from the main repo; the app itself reads updates from here.
        private const string GithubRepoUrl = "https://github.com/0slvr/SteelSeriesDiscordStreamFix-updates";

        // The app almost never exits on its own, so a single check at startup would leave long-running
        // installs unaware of new versions for weeks. Re-check regularly; retry sooner after a failure
        // (typically no network yet right after Windows login).
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

        // Background checks, manual checks and the download must never run at the same time.
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private static volatile bool _notInstalled;
        private static volatile bool _lastCheckFailed;

        private static UpdateManager _manager;
        private static UpdateInfo _info;
        private static string _notifiedVersion;

        /// <summary>
        /// Version that is available on the server and newer than the running one (not downloaded yet).
        /// Null when the app is up to date or has not checked yet.
        /// </summary>
        public static string AvailableVersion { get; private set; }

        /// <summary>
        /// Raised (from a background thread) when a background check finds a version the user has not
        /// skipped, once per version per app run. Subscribers must marshal to the UI thread themselves.
        /// </summary>
        public static event Action<string> UpdateFound;

        /// <summary>
        /// Set by the app: closes the app through its NORMAL exit path (on the UI thread), so cleanup runs
        /// first (Sonar devices restored, Discord un-muted). Used to apply an update.
        /// </summary>
        public static Action RequestExit { get; set; }

        public static bool IsSkipped(string version)
        {
            return !string.IsNullOrEmpty(version) &&
                   string.Equals(AppSettings.SkippedUpdateVersion, version, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Skip this version": no more notifications for it. A newer version notifies again.</summary>
        public static void SkipAvailable()
        {
            var v = AvailableVersion;
            if (v == null) return;
            AppSettings.SkippedUpdateVersion = v;
            Logger.Log("AppUpdater: user skipped version " + v + ".");
        }

        /// <summary>Checks for updates now and then every few hours. Never throws, never installs anything.</summary>
        public static async Task RunPeriodicChecksAsync(Func<bool> isExiting)
        {
            while (!isExiting())
            {
                await CheckForUpdateAsync(background: true, statusCallback: null);

                if (_notInstalled) return; // running from source, nothing to check

                try { await Task.Delay(_lastCheckFailed ? RetryAfterFailure : CheckInterval); }
                catch { return; }
            }
        }

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(bool background, Action<string> statusCallback)
        {
            await Gate.WaitAsync();
            try
            {
                _lastCheckFailed = false;
                statusCallback?.Invoke("Checking for updates...");

                var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));
                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    _manager = null;
                    _info = null;
                    AvailableVersion = null;
                    Logger.Log("AppUpdater" + (background ? "" : " (manual)") + ": no update available.");
                    return UpdateCheckResult.UpToDate;
                }

                string version = newVersion.TargetFullRelease.Version.ToString();
                _manager = mgr;
                _info = newVersion;
                AvailableVersion = version;
                Logger.Log("AppUpdater" + (background ? "" : " (manual)") + ": update found -> " + version);

                if (background)
                {
                    if (IsSkipped(version))
                    {
                        Logger.Log("AppUpdater: version " + version + " was skipped by the user, not notifying.");
                    }
                    else if (_notifiedVersion != version)
                    {
                        _notifiedVersion = version;
                        try { UpdateFound?.Invoke(version); }
                        catch (Exception ex) { Logger.Log("AppUpdater: UpdateFound handler failed: " + ex.Message); }
                    }
                }

                return UpdateCheckResult.UpdateAvailable;
            }
            catch (NotInstalledException)
            {
                _notInstalled = true;
                Logger.Log("AppUpdater: running from source/dev (not installed via Velopack), skipping update check.");
                return UpdateCheckResult.NotInstalled;
            }
            catch (Exception ex)
            {
                _lastCheckFailed = true;
                Logger.Log("AppUpdater failed: " + ex);
                return UpdateCheckResult.Error;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Downloads the version found by the last check and installs it. The app is closed through its normal
        /// exit path (so Sonar devices are restored and Discord is un-muted first), the update is installed and
        /// the new version starts. Returns false if anything failed; the app then keeps running as before.
        /// </summary>
        public static async Task<bool> DownloadAndApplyAsync(Action<string> statusCallback, string[] restartArgs)
        {
            await Gate.WaitAsync();
            try
            {
                var mgr = _manager;
                var info = _info;
                if (mgr == null || info == null)
                {
                    Logger.Log("AppUpdater: DownloadAndApply called without a pending update.");
                    return false;
                }

                statusCallback?.Invoke("Downloading update " + AvailableVersion + "...");
                await mgr.DownloadUpdatesAsync(info);

                statusCallback?.Invoke("Restarting to apply update...");
                ApplyAfterNormalExit(mgr, info, restartArgs);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("AppUpdater download/apply failed: " + ex);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

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
        UpdateAvailable,
        NotInstalled,
        Error
    }
}

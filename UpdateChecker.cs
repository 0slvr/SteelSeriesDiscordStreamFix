using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Holds info about a newer version, parsed from the plain-text manifest.
    /// </summary>
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string Url;
        public string Sha256;   // lowercase hex, may be null if manifest omits it
        public string Notes;
    }

    /// <summary>
    /// Quiet, background update checker. Never shows any UI on its own —
    /// it only sets PendingUpdate (read by Form1's RefreshUpdateStatus)
    /// and raises OnUpdateAvailable exactly once per newly-seen version
    /// (used by MicMuterContext to pop a Windows balloon-tip "toast" so
    /// the user doesn't have to open Settings to find out). The actual
    /// download + install only ever happens after the user explicitly
    /// clicks the link/toast and confirms.
    ///
    /// Manifest format is deliberately plain text (not JSON) to avoid
    /// pulling in an extra dependency for a single-file utility app:
    ///
    ///   line 1: version number, e.g. "1.3.0"
    ///   line 2: direct download URL to the new .exe
    ///   line 3: SHA256 of that exe, as a hex string (optional — leave the
    ///           line blank to skip verification, but this is not
    ///           recommended)
    ///   line 4+: optional release notes (shown only in the confirmation
    ///            dialog)
    /// </summary>
    internal static class UpdateChecker
    {
        // TODO: point this at a URL you control — e.g. a raw file in a
        // GitHub repo, or anything returning plain text over HTTPS.
        private const string ManifestUrl = "https://api.github.com/repos/0slvr/SteelSeriesDiscordStreamFix/releases/latest";

        private const int InitialDelaySeconds = 8;      // let startup settle first
        private static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(24);

        // Shared HttpClient instance — avoids socket exhaustion from
        // repeatedly creating/disposing HttpClient on every check.
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private static bool _started;

        // Tracks the highest version we've already raised OnUpdateAvailable
        // for, so the 24h recheck loop doesn't re-pop a toast for the same
        // pending version over and over.
        private static Version _lastNotifiedVersion;

        public static UpdateInfo PendingUpdate { get; private set; }

        /// <summary>
        /// Raised exactly once whenever a newer version than anything
        /// previously notified is found. Subscribers get called from a
        /// background thread — marshal to the UI thread before touching
        /// any Windows Forms controls.
        /// </summary>
        public static event Action<UpdateInfo> OnUpdateAvailable;

        /// <summary>
        /// Starts the background check-and-reschedule loop. Safe to call
        /// once per process; later calls are ignored. Never throws and
        /// never blocks the caller.
        /// </summary>
        public static void Start()
        {
            if (_started) return;
            _started = true;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(InitialDelaySeconds));
                    while (true)
                    {
                        await CheckOnceAsync();
                        await Task.Delay(RecheckInterval);
                    }
                }
                catch (Exception ex)
                {
                    // The whole point of this feature is to never be
                    // disruptive — a failure here is logged and dropped,
                    // not surfaced to the user.
                    Logger.Log("UpdateChecker loop stopped unexpectedly: " + ex);
                }
            });
        }

        /// <summary>
        /// Normalizes a Version to always have 4 non-negative components.
        /// Version.TryParse("1.3.0", ...) leaves Revision as -1, while an
        /// assembly version like 1.3.0.0 has Revision = 0. Without this,
        /// a manifest version with fewer components than the assembly
        /// version could incorrectly compare as "older" even when equal,
        /// silently swallowing a real update.
        /// </summary>
        private static Version Normalize(Version v) =>
            new Version(
                Math.Max(v.Major, 0),
                Math.Max(v.Minor, 0),
                Math.Max(v.Build, 0),
                Math.Max(v.Revision, 0));

        private static async Task CheckOnceAsync()
        {
            try
            {
                string text = await _http.GetStringAsync(ManifestUrl);
                string[] lines = text.Replace("\r\n", "\n").Split('\n');

                if (lines.Length < 2)
                {
                    Logger.Log("UpdateChecker: manifest malformed (need at least 2 lines).");
                    return;
                }

                string versionStr = lines[0].Trim();
                string url = lines[1].Trim();
                string sha256 = lines.Length > 2 ? lines[2].Trim() : "";
                string notes = lines.Length > 3 ? string.Join(Environment.NewLine, lines, 3, lines.Length - 3).Trim() : "";

                if (!Version.TryParse(versionStr, out Version remote) || string.IsNullOrWhiteSpace(url))
                {
                    Logger.Log($"UpdateChecker: could not parse manifest (version='{versionStr}', url='{url}').");
                    return;
                }

                if (!string.IsNullOrEmpty(sha256) && !IsValidSha256Hex(sha256))
                {
                    Logger.Log($"UpdateChecker: manifest SHA256 field is not valid hex, ignoring it: '{sha256}'.");
                    sha256 = "";
                }

                Version current = Assembly.GetExecutingAssembly().GetName().Version;

                if (Normalize(remote) > Normalize(current))
                {
                    Logger.Log($"UpdateChecker: update available {current} -> {remote}");

                    var info = new UpdateInfo
                    {
                        Version = remote,
                        Url = url,
                        Sha256 = string.IsNullOrEmpty(sha256) ? null : sha256.ToLowerInvariant(),
                        Notes = notes
                    };
                    PendingUpdate = info;

                    bool alreadyNotified = _lastNotifiedVersion != null && Normalize(remote) <= Normalize(_lastNotifiedVersion);
                    if (!alreadyNotified)
                    {
                        _lastNotifiedVersion = remote;
                        try
                        {
                            OnUpdateAvailable?.Invoke(info);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("UpdateChecker: OnUpdateAvailable subscriber threw: " + ex);
                        }
                    }
                }
                else
                {
                    PendingUpdate = null;
                }
            }
            catch (Exception ex)
            {
                // Network hiccups, manifest URL not set up yet, etc. — just
                // log and try again on the next scheduled check.
                Logger.Log("UpdateChecker.CheckOnceAsync failed: " + ex.Message);
            }
        }

        private static bool IsValidSha256Hex(string s)
        {
            if (s.Length != 64) return false;
            foreach (char c in s)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }

        private static string ComputeSha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Downloads the new exe, verifies its SHA256 against the manifest
        /// (when the manifest provided one), and hands off to a tiny helper
        /// script that: waits for this process to exit, replaces the exe,
        /// relaunches it, then deletes only itself (never the exe) once
        /// done. Only ever called from a user click — never automatically.
        ///
        /// If the manifest did not include a hash, the download proceeds
        /// without verification (logged as a warning) rather than blocking
        /// the update entirely — but supplying a hash in the manifest is
        /// strongly recommended.
        /// </summary>
        public static async Task DownloadAndApplyAsync(UpdateInfo info, Action<string> onError)
        {
            try
            {
                byte[] data = await _http.GetByteArrayAsync(info.Url);

                if (!string.IsNullOrEmpty(info.Sha256))
                {
                    string actual = ComputeSha256Hex(data);
                    if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        string msg = $"Downloaded file hash mismatch (expected {info.Sha256}, got {actual}). Update aborted.";
                        Logger.Log("UpdateChecker.DownloadAndApplyAsync: " + msg);
                        onError?.Invoke(msg);
                        return;
                    }
                    Logger.Log("UpdateChecker: SHA256 verified OK.");
                }
                else
                {
                    Logger.Log("UpdateChecker: no SHA256 in manifest — skipping verification (not recommended).");
                }

                string tempExe = Path.Combine(Path.GetTempPath(), "SSDSF_Update_" + Guid.NewGuid().ToString("N") + ".exe");
                File.WriteAllBytes(tempExe, data);

                string currentExe = Application.ExecutablePath;
                string exeName = Path.GetFileName(currentExe);
                string batPath = Path.Combine(Path.GetTempPath(), "SSDSF_ApplyUpdate_" + Guid.NewGuid().ToString("N") + ".bat");

                string script =
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    $"tasklist /fi \"imagename eq {exeName}\" | find /i \"{exeName}\" >nul\r\n" +
                    "if not errorlevel 1 (\r\n" +
                    "  timeout /t 1 /nobreak >nul\r\n" +
                    "  goto wait\r\n" +
                    ")\r\n" +
                    $"copy /y \"{tempExe}\" \"{currentExe}\" >nul\r\n" +
                    $"del \"{tempExe}\" >nul 2>&1\r\n" +
                    $"start \"\" \"{currentExe}\"\r\n" +
                    "(goto) 2>nul & del \"%~f0\"\r\n"; // self-delete the helper script only

                File.WriteAllText(batPath, script);

                Logger.Log($"UpdateChecker: launching updater script, applying {info.Version}.");

                var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);

                Application.Exit();
            }
            catch (Exception ex)
            {
                Logger.Log("UpdateChecker.DownloadAndApplyAsync failed: " + ex);
                onError?.Invoke(ex.Message);
            }
        }
    }
}
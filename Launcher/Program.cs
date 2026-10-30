using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace SteelSeriesDiscordStreamFix.Launcher
{
    internal static class Program
    {
        private const string Title = "SteelSeries Discord Stream Fix";

        /// <summary>
        /// One file, three behaviours:
        ///   not installed          -> options window; Save installs and starts the app
        ///   installed, older       -> "Update to X?"  Yes = update, No = open the installed app
        ///   installed, same/newer  -> open the installed app (its Settings window)
        /// </summary>
        [STAThread]
        private static int Main()
        {
            TryEnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool created;
            using (var mutex = new Mutex(true, "SteelSeriesDiscordStreamFix_Launcher_Mutex", out created))
            {
                if (!created) return 0; // already open (e.g. double click)

                try
                {
                    return Run();
                }
                catch (Exception ex)
                {
                    Log.Write("FATAL: " + ex);
                    MessageBox.Show("Something went wrong: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }

        private static int Run()
        {
            Version mine = Assembly.GetExecutingAssembly().GetName().Version;
            var thisVersion = new Version(mine.Major, mine.Minor, mine.Build);
            InstalledApp installed = InstalledApp.Detect();
            Log.Write("Launcher " + thisVersion + ", installed: " + (installed == null ? "none" : installed.Version.ToString()));

            if (installed == null)
            {
                if (!Installer.HasEmbeddedSetup())
                {
                    MessageBox.Show("This is a development build without the embedded installer.", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 1;
                }

                using (var form = new SetupForm(thisVersion.ToString()))
                {
                    Application.Run(form);
                    if (form.Installed)
                    {
                        MessageBox.Show("Version " + thisVersion + " is installed and protecting Discord in the background.\n\n" +
                                        "Open this file again any time to change the settings.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                return 0;
            }

            if (installed.Version < thisVersion && Installer.HasEmbeddedSetup())
            {
                var answer = MessageBox.Show(
                    "Version " + installed.Version + " is installed. This file contains version " + thisVersion + ".\n\n" +
                    "Update now?  (Your settings are kept.)\n\n" +
                    "Yes = update       No = open the installed version without updating",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);

                if (answer == DialogResult.Yes)
                {
                    string error = null;
                    using (var wait = new BusyForm("Updating to version " + thisVersion + "..."))
                    {
                        wait.Shown += async (s, e) =>
                        {
                            error = await System.Threading.Tasks.Task.Run(() => Installer.Install(null));
                            wait.Close();
                        };
                        Application.Run(wait);
                    }

                    if (error == null)
                        MessageBox.Show("Updated to version " + thisVersion + ". Your settings were kept.", Title,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else
                        MessageBox.Show(error, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return error == null ? 0 : 1;
                }
            }

            // Same version, newer version, or the user said "no": just open what is installed.
            InstalledApp.Launch(null);
            return 0;
        }

        private static void TryEnableDpiAwareness()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4) /* PER_MONITOR_AWARE_V2 */); }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }
}

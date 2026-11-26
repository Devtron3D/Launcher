using System;
using System.Reflection;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LauncherApp
{
    static class AppInfo
    {
        // Comes from <Version> in LauncherApp.csproj, so it only has to be changed there.
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return "v" + v.Major + "." + v.Minor;
            }
        }

        // True when the launcher runs with administrator rights (then every program it starts does too).
        public static readonly bool IsElevated = CheckElevated();

        static bool CheckElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    static class Program
    {
        // Passed by "Restart as admin": the old launcher is still closing, so wait for it instead of warning.
        public const string RestartedArg = "--restarted";

        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // One launcher per data folder: two would each keep their own copy of the profiles, and the one that
            // saves last would undo the other's changes.
            Mutex mutex = null;
            bool owned = false;
            try
            {
                bool restarted = args.Any(a => a == RestartedArg);
                try
                {
                    mutex = new Mutex(false, @"Local\Launcher-" + FolderKey(PathUtil.HomeDir));
                    owned = Acquire(mutex, restarted ? 15000 : 0);
                }
                catch (UnauthorizedAccessException)
                {
                    // Made by a launcher running as admin, which this one may not open: so one is open.
                }
                if (!owned)
                {
                    var r = MessageBox.Show(
                        "The launcher is already open from this folder:\n" + PathUtil.Full(PathUtil.HomeDir) +
                        "\n\nTwo launchers on the same profiles can undo each other's changes. Open a second one anyway?",
                        "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
                    if (r != DialogResult.Yes) return;
                }
                // The temp folder is only emptied when no other launcher (or programs of the one just restarted
                // as admin) can be using it.
                Application.Run(new MainForm(args, owned && !restarted));
            }
            finally
            {
                if (mutex != null)
                {
                    if (owned) try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }

        static bool Acquire(Mutex m, int waitMs)
        {
            try { return m.WaitOne(waitMs); }
            catch (AbandonedMutexException) { return true; }   // the other launcher ended without letting go
        }

        static string FolderKey(string dir)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(PathUtil.Trim(dir).ToUpperInvariant()));
                return BitConverter.ToString(bytes, 0, 10).Replace("-", "");
            }
        }

        static bool showingError;

        // Something unexpected went wrong: say so in plain words instead of .NET's crash dialog. The message
        // can be copied with Ctrl+C. Nothing is written to the PC.
        static void ShowError(Exception ex)
        {
            if (showingError) return;
            showingError = true;
            try
            {
                MessageBox.Show(
                    "Something went wrong in the launcher:\n\n" + (ex == null ? "unknown error" : ex.Message) +
                    "\n\nPrograms it started are not affected. If this keeps happening, press Ctrl+C to copy this " +
                    "message and send it along.\n\n" + (ex == null ? "" : ex.GetType().Name + "\n" + ex.StackTrace),
                    "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            finally
            {
                showingError = false;
            }
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TinyX;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = LaunchOptions.Parse(args);
        AppLog.Initialize(options.IsSmokeTest, options.InspectProfile);
        Application.ThreadException += (_, e) => AppLog.Error("ui-unhandled", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("process-unhandled", e.ExceptionObject as Exception);

        string userScope = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserName + "|" + Environment.MachineName)))[..16];
        if (options.DiagnosticsExisting || options.ExitExisting)
        {
            string action = options.DiagnosticsExisting ? "Export" : "Exit";
            try
            {
                using EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\TinyX." + action + "." + userScope);
                signal.Set();
                AppLog.Info("existing-instance-command", "action=" + action);
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                AppLog.Error("existing-instance-command-unavailable", ex);
                Environment.ExitCode = 2;
            }
            return;
        }
        string instanceScope = options.InspectProfile ? "Inspect." + Environment.ProcessId :
            options.IsSmokeTest ? "Test." + Environment.ProcessId : userScope;
        using var mutex = new Mutex(true, "Local\\TinyX.Instance." + instanceScope, out bool firstInstance);
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\TinyX.Activate." + instanceScope);
        using var export = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\TinyX.Export." + instanceScope);
        using var exit = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\TinyX.Exit." + instanceScope);
        if (!firstInstance)
        {
            activation.Set();
            return;
        }

        var waiters = new List<RegisteredWaitHandle>();
        try
        {
            using var form = new MainForm(options);
            void RegisterAction(EventWaitHandle signal, Action action)
            {
                waiters.Add(ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                    {
                        try { form.BeginInvoke(action); }
                        catch (InvalidOperationException) { }
                    }
                }, null, Timeout.Infinite, false));
            }
            RegisterAction(activation, form.ShowWindow);
            RegisterAction(export, () => _ = form.ExportCurrentDiagnosticsAsync());
            RegisterAction(exit, form.RequestExit);
            AppLog.Info("startup", "smoke=" + options.IsSmokeTest);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            AppLog.Error("startup-failed", ex);
            Environment.ExitCode = 1;
            if (!options.InspectProfile)
                MessageBox.Show("TinyX 无法启动。请查看 %LOCALAPPDATA%\\TinyX\\logs 下的日志。",
                    "TinyX", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (RegisteredWaitHandle waiter in waiters) waiter.Unregister(null);
            mutex.ReleaseMutex();
            AppLog.Info("shutdown");
        }
    }
}

internal sealed record LaunchOptions(bool IsSmokeTest, bool LocalFixture, string? DiagnosticsPath,
    bool InspectProfile, bool DiagnosticsExisting, bool ExitExisting)
{
    public bool IsTransient => IsSmokeTest || InspectProfile;

    public static LaunchOptions Parse(string[] args)
    {
        const string smokeArgument = "--smoke-test";
        const string localArgument = "--smoke-test-local";
        const string diagnosticsArgument = "--diagnostics";
        bool smoke = args.Contains(smokeArgument, StringComparer.OrdinalIgnoreCase);
        bool local = args.Contains(localArgument, StringComparer.OrdinalIgnoreCase);
        string? diagnostics = null;
        int index = Array.FindIndex(args, value => value.Equals(diagnosticsArgument,
            StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < args.Length)
            diagnostics = Path.GetFullPath(args[index + 1]);
        return new LaunchOptions(smoke || local, local, diagnostics,
            args.Contains("--inspect-profile", StringComparer.OrdinalIgnoreCase),
            args.Contains("--diagnostics-existing", StringComparer.OrdinalIgnoreCase),
            args.Contains("--exit-existing", StringComparer.OrdinalIgnoreCase));
    }
}

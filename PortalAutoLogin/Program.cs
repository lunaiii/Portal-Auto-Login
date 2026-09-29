using System.Threading;

namespace PortalAutoLogin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\PortalAutoLogin-192.168.6.2", out bool firstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\PortalAutoLogin-192.168.6.2-Show");

        if (!firstInstance)
        {
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        using var form = new MainForm(args.Contains("--background", StringComparer.OrdinalIgnoreCase));
        using var stop = new CancellationTokenSource();
        var signalThread = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (!showEvent.WaitOne(500)) continue;
                try
                {
                    if (form.IsHandleCreated && !form.IsDisposed)
                        form.BeginInvoke(form.ShowSettings);
                }
                catch (InvalidOperationException) { }
            }
        }) { IsBackground = true, Name = "Show settings signal" };
        signalThread.Start();

        Application.Run(form);
        stop.Cancel();
    }
}

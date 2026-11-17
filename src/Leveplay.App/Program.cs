namespace Leveplay.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // A second instance would fight over the OAuth callback port.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Leveplay", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        // Log instead of showing the "Continue / Quit" dialog, which a failing timer
        // would reopen on every tick.
        Application.ThreadException += (_, e) => AppLog.Write($"unhandled: {e.Exception}");
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new Shell());
    }
}

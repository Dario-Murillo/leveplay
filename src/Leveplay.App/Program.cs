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
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.Run(new Shell());
    }
}

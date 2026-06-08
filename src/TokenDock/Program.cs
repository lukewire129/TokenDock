using System;
using System.Threading;
using Avalonia;
using Luke.Mvux.Avalonia;

namespace TokenDock
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            using var singleInstanceMutex = new Mutex(true, @"Global\TokenDock.SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UseMvux()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HotkeyTest;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		var logger = new FileLogger();
		logger.Info("アプリ起動");

		BuildAvaloniaApp(logger).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
	}

	private static AppBuilder BuildAvaloniaApp(FileLogger logger)
	{
		return AppBuilder
			.Configure(() => new App(logger))
			.UsePlatformDetect()
			.LogToTrace();
	}
}

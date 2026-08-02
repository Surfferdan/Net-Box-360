using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			LogException(args.ExceptionObject as Exception, "AppDomain");
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			LogException(args.Exception, "TaskScheduler");
			args.SetObserved();
		};
		base.OnStartup(e);
		MainWindow mainWindow = new MainWindow();
		MainWindow = mainWindow;
		mainWindow.Show();
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogException(e.Exception, "Dispatcher");
		e.Handled = true;
	}

	internal static void LogException(Exception? exception, string source)
	{
		if (exception == null)
		{
			return;
		}

		try
		{
			File.AppendAllText(
				Path.Combine(AppPaths.LogsFolder, "crash.log"),
				$"[{DateTimeOffset.Now:u}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch
		{
		}
	}

}

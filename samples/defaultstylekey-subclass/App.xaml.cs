using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DefaultStyleKeySubclassSample;

public partial class App : Application
{
	public App()
	{
		this.DispatcherUnhandledException += App_DispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
	}

	private void App_DispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
	{
		try
		{
			SampleLog.Write($"[DispatcherUnhandledException] {e.Exception}");
		}
		catch { }
	}

	private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		try
		{
			if (e.ExceptionObject is Exception ex)
				SampleLog.Write($"[CurrentDomainUnhandledException] {ex}");
			else
				SampleLog.Write($"[CurrentDomainUnhandledException] {e.ExceptionObject}");
		}
		catch { }
	}

	private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		try
		{
			SampleLog.Write($"[UnobservedTaskException] {e.Exception}");
			e.SetObserved();
		}
		catch { }
	}
}

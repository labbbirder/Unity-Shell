// #define DETECT_STDOUT_ENCODING

using System.Diagnostics;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace com.bbbirder.unityeditor
{
	public enum LogEventType
	{
		InfoLog,
		WarnLog,
		ErrorLog,
		EndStream,
	}


	public struct ShellSettings
	{
		public string workDirectory;
		public Dictionary<string, string> environment;
		public bool quiet;
		public bool throwOnNonZeroExitCode;
		/// <summary>
		/// Show Unity progress bar while executing.
		/// </summary>
		public bool withProgress;
		/// <summary>
		/// Whether the Unity progress bar can be canceled.
		/// </summary>
		public bool progressCancelable;
		/// <summary>
		/// Whether to clear the Unity progress with the shell terminated.
		/// </summary>
		public bool autoClearProgress;

		public static ShellSettings Default => new()
		{
			workDirectory = ".",
			environment = null,
			quiet = false,
			withProgress = false,
			progressCancelable = true,
			autoClearProgress = true,
			throwOnNonZeroExitCode = true,
		};
	}


	public static class Shell
	{
		public static Dictionary<string, string> DefaultEnvironment = new();
		internal static ConcurrentQueue<(ShellRequest req, LogEventType type, object arg)> queue = new();
		public static Dictionary<ShellRequest, StringBuilder> lineBuilders = new();

		static Shell()
		{
			EditorApplication.update += DumpQueue;
		}

		internal static void DumpQueue()
		{
			while (queue.TryDequeue(out var res))
			{
				try
				{
					var (req, type, arg) = res;
					req.OnReceiveData(type, arg);
				}
				catch (Exception e)
				{
					UnityEngine.Debug.LogException(e);
				}
			}
		}

		/// <summary>
		/// Whether the command tool exists on this machine.
		/// </summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public static bool ExistsCommand(string command)
		{
			bool isInPath = false;
			foreach (string test in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
			{
				string path = test.Trim();
				if (!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, command)))
				{
					isInPath = true;
					break;
				}
			}

			return isInPath;
		}


		static void ApplyEnviron(ProcessStartInfo start, Dictionary<string, string> environ)
		{
			if (environ == null) return;
			foreach (var (name, val) in environ)
			{
				if (name.ToUpperInvariant().Equals("PATH"))
				{
					var pathes = Environment.GetEnvironmentVariable("PATH") ?? "";
					var additional = val.Split(ConsoleUtils.ANY_PATH_SPLITTER);
					pathes = string.Join(ConsoleUtils.PATH_SPLITTER, additional) + ConsoleUtils.PATH_SPLITTER + pathes;
					start.EnvironmentVariables["PATH"] = pathes;
				}
				else
				{
					start.EnvironmentVariables[name] = val;
				}
			}
		}

		static Process CreateProcess(string cmd, string workDirectory = ".", Dictionary<string, string> environ = null, params object[] args)
		{
			ProcessStartInfo start = new ProcessStartInfo(cmd)
			{
				CreateNoWindow = true,
				ErrorDialog = true,
				UseShellExecute = false,
				WorkingDirectory = workDirectory,
			};

			start.RedirectStandardOutput =
			start.RedirectStandardError =
			start.RedirectStandardInput = true;

			// 			start.StandardInputEncoding =
			// 			start.StandardOutputEncoding =
			// 			start.StandardErrorEncoding =
			// #if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
			// 				Encoding.Unicode;
			// #else
			// 				Encoding.UTF8;
			// #endif

			start.ArgumentList.Clear();
			foreach (var arg in args)
			{
				start.ArgumentList.Add(arg.ToString());
			}

			ApplyEnviron(start, DefaultEnvironment);
			ApplyEnviron(start, environ);
			var process = new Process()
			{
				StartInfo = start,
			};
			process.Start();
			return process;
		}


		/// <summary>
		/// Run a command
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="workDirectory"></param>
		/// <param name="environmentVars"></param>
		/// <returns></returns>
		public static ShellRequest RunCommand(string cmd, ShellSettings? settings = default)
		{
			var settings2 = settings ?? ShellSettings.Default;
			var workDirectory = settings2.workDirectory ?? ".";
			var environ = settings2.environment ?? new();
			var quiet = settings2.quiet;
			var finalCmd =
#if UNITY_EDITOR_WIN
				"@echo off>nul\n" +
#endif
#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
				"@chcp 65001>nul\n"
#endif
				cmd;

			var tempFile = Path.GetTempFileName();
			var cmdFile = tempFile +
#if UNITY_EDITOR_WIN
				".bat";
#else
				".sh";
#endif
			if (File.Exists(cmdFile))
			{
				File.Delete(cmdFile);
			}
			File.Move(tempFile, cmdFile);
			File.WriteAllText(cmdFile, finalCmd);
#if UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
			var ret = os_chmod(cmdFile, 511);
			var p = CreateProcess("bash", workDirectory, environ, cmdFile);
#else
			var p = CreateProcess(cmdFile, workDirectory, environ);
#endif

			return QueueUpProcess(p, cmd, settings2);
		}


		public static ShellRequest RunCommand(string executable, ShellSettings settings, params object[] args)
		{
			var workDirectory = settings.workDirectory ?? ".";
			var environ = settings.environment;

			var p = CreateProcess(executable, workDirectory, environ, args);
			return QueueUpProcess(p, executable + ' ' + String.Join(' ', args), settings);
		}


		public static ShellRequest RunCommand(string executable, params object[] args)
		{
			return RunCommand(executable, ShellSettings.Default, args);
		}


		static ShellRequest QueueUpProcess(Process p, string cmd, ShellSettings settings)
		{

			ShellRequest req = new ShellRequest(cmd, settings, p);

			ThreadPool.QueueUserWorkItem(delegate (object state)
			{
				try
				{
					var builder = new StringBuilder();

					while (!p.StandardOutput.EndOfStream)
					{
						while (p.StandardOutput.Peek() > -1)
						{
							builder.Append((char)p.StandardOutput.Read());
						}
						var output = builder.ToString();
						builder.Clear();
						queue.Enqueue((req, LogEventType.InfoLog, output));
					}

					while (!p.StandardError.EndOfStream)
					{
						string error = p.StandardError.ReadLine();

						if (!string.IsNullOrEmpty(error))
						{
							if (!string.IsNullOrEmpty(error))
								queue.Enqueue((req, LogEventType.ErrorLog, error));
						}
					}

					queue.Enqueue((req, LogEventType.EndStream, p.ExitCode));

				}
				catch (Exception e)
				{
					UnityEngine.Debug.LogException(new("shell execute fail", e));
				}
				finally
				{
					if (p != null)
					{
						p.Close();
						p = null;
					}
				}
			});
			return req;
		}


		#region external stubs
		[DllImport("libOS.so", EntryPoint = "os_chmod")]
		extern static int os_chmod(string path, int mode);
		#endregion
	}

}

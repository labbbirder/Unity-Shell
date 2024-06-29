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
using System.Buffers;
using System.Linq;

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
		readonly static Encoding DEFAULT_WINDOWS_CONSOLE_ENCODING = Encoding.GetEncoding("gbk");
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

			start.StandardInputEncoding =
			start.StandardOutputEncoding =
			start.StandardErrorEncoding =
#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
				Encoding.Unicode;
#else
				Encoding.UTF8;
#endif

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
		/// Run multi-line command
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
				"@chcp 65001>nul\n" +
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
			var ret = chmod(cmdFile, 511);
			var p = CreateProcess("bash", workDirectory, environ, cmdFile);
#else
			var p = CreateProcess(cmdFile, workDirectory, environ);
#endif

			return QueueUpProcess(p, cmd, settings2);
		}


		public static ShellRequest RunCommandLine(string executable, ShellSettings settings, params object[] args)
		{
			var workDirectory = settings.workDirectory ?? ".";
			var environ = settings.environment;

			var p = CreateProcess(executable, workDirectory, environ, args);
			return QueueUpProcess(p, executable + ' ' + String.Join(' ', args), settings);
		}


		public static ShellRequest RunCommandLine(string executable, params object[] args)
		{
			return RunCommandLine(executable, ShellSettings.Default, args);
		}

		static IEnumerable<string> GetConsoleOutput(StreamReader console)
		{
			const int BUFFER_SIZE = 40960;
			var buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
			while (!console.EndOfStream)
			{
				var index = 0;
				var wchar = console.CurrentEncoding == Encoding.Unicode;
				while (console.Peek() != -1)
				{
					var b = console.Read();
					if (wchar)
					{
						if (index + 2 < buffer.Length)
						{
							if (BitConverter.IsLittleEndian)
							{
								buffer[index++] = (byte)(b & 0xff);
								buffer[index++] = (byte)(b >> 8);
							}
							else
							{
								buffer[index++] = (byte)(b >> 8);
								buffer[index++] = (byte)(b & 0xff);
							}
						}
					}
					else
					{
						if (index + 1 < buffer.Length)
						{
							buffer[index++] = (byte)b;
						}
					}
				}

				var encoding = Encoding.UTF8;
#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
				// UnityEngine.Debug.Log(string.Join(',', buffer.Select(b => b.ToString("x2")).ToArray(), 0, index));
				// UnityEngine.Debug.Log(ConsoleUtils.IsValidUTF8(buffer, 0, index) + " " + index);
				if (!ConsoleUtils.IsValidUTF8(buffer, 0, index))
				{
					encoding = DEFAULT_WINDOWS_CONSOLE_ENCODING;
				}
#endif
				var output = encoding.GetString(buffer, 0, index);
				yield return output;
			}
			ArrayPool<byte>.Shared.Return(buffer);
		}

		static ShellRequest QueueUpProcess(Process p, string cmd, ShellSettings settings)
		{

			ShellRequest req = new ShellRequest(cmd, settings, p);

			ThreadPool.QueueUserWorkItem(delegate (object state)
			{
				try
				{
					foreach (var output in GetConsoleOutput(p.StandardOutput))
					{
						queue.Enqueue((req, LogEventType.InfoLog, output));
					}

					foreach (var output in GetConsoleOutput(p.StandardError))
					{
						if (!string.IsNullOrEmpty(output))
							queue.Enqueue((req, LogEventType.ErrorLog, output));
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
		[DllImport("libc")]
		extern static int chmod(string path, int mode);
		#endregion
	}

}

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
					if (type == LogEventType.EndStream)
					{
						if (!lineBuilders.TryGetValue(req, out var builder))
						{
							lineBuilders[req] = builder = new();
						}
						if (builder.Length > 0)
						{
							req.Log(LogEventType.InfoLog, builder.ToString());
						}
						builder.Clear();
						req.NotifyComplete((int)arg);
					}
					else
					{
						if (type == LogEventType.InfoLog)
						{
							if (!lineBuilders.TryGetValue(req, out var builder))
							{
								lineBuilders[req] = builder = new();
							}
							var buf = (arg as string).AsSpan();
							var nIdx = ~0;
							var pendingOutput = "";
							while ((nIdx = buf.IndexOf('\n')) != ~0)
							{
								builder.Append(buf.Slice(0, nIdx));
								buf = buf.Slice(nIdx + 1);
								pendingOutput = builder.ToString();
								req.Log(type, pendingOutput);
								builder.Clear();
							}
							builder.Append(buf);
							if (builder.Length > 0)
							{
								pendingOutput = builder.ToString();
							}
							req.PendingOutput = pendingOutput;
						}
						else
						{
							req.Log(type, (string)arg);
						}
					}
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
		[DllImport("libOS.so", EntryPoint = "os_chmod")]
		extern static int os_chmod(string path, int mode);

		static Process CreateProcess(string cmd, string workDirectory = ".", Dictionary<string, string> environ = null, params string[] args)
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
				start.ArgumentList.Add(arg);
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
		public static ShellRequest RunCommand(string cmd, ShellSettings settings = default)
		{
			var workDirectory = settings.workDirectory ?? ".";
			var environ = settings.environment ?? new();
			var quiet = settings.quiet;
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

			return QueueUpProcess(p, cmd, quiet, settings.throwOnNonZeroExitCode);
		}


		public static ShellRequest RunCommand(string executable, ShellSettings settings = default, params string[] args)
		{
			var workDirectory = settings.workDirectory ?? ".";
			var environ = settings.environment ?? new();
			var quiet = settings.quiet;

			var p = CreateProcess(executable, workDirectory, environ, args);
			return QueueUpProcess(p, executable + ' ' + String.Join(' ', args), quiet, settings.throwOnNonZeroExitCode);
		}


		static ShellRequest QueueUpProcess(Process p, string cmd, bool quiet, bool throwOnNonZeroExitCode)
		{

			ShellRequest req = new ShellRequest(cmd, p, quiet, throwOnNonZeroExitCode);

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
	}

}

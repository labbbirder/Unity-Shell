// #define DETECT_STDOUT_ENCODING

using System.Diagnostics;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

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
						req.NotifyComplete((int)arg);
					}
					else
					{
						req.Log(type, (string)arg);
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


		static Process CreateProcess(string cmd, string workDirectory = ".", Dictionary<string, string> environ = null, params string[] args)
		{
			ProcessStartInfo start = new ProcessStartInfo(cmd)
			{
				CreateNoWindow = true,
				ErrorDialog = true,
				UseShellExecute = false,
				WorkingDirectory = workDirectory,
			};

			if (start.UseShellExecute)
			{
				start.RedirectStandardOutput =
				start.RedirectStandardError =
				start.RedirectStandardInput = false;
			}
			else
			{
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
			}

			start.ArgumentList.Clear();
			foreach (var arg in args)
			{
				start.ArgumentList.Add(arg);
			}

			ApplyEnviron(start, DefaultEnvironment);
			ApplyEnviron(start, environ);

			return Process.Start(start);
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
			var finalCmd = "@echo off>nul\n" +
#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
				"@chcp 65001>nul\n"
#endif
				cmd;
			Process p = null;
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
			File.WriteAllText(cmdFile, cmd);
#if UNITY_EDITOR_LINUX || UNITY_EDITOR_OSX
			var pChmod = CreateProcess("chmod", ".", "+x", cmdFile);
			pChmod.WaitForExit();
#endif
			// #if UNITY_EDITOR_WIN
			// #if DETECT_STDOUT_ENCODING
			// 			start.Arguments = "/u /c \"chcp 65001>nul&" + cmd + " \"";
			// #else
			// 			start.Arguments = "/c \"" + cmd + " \"";
			// #endif
			// #else
			// 			start.Arguments += "-c \"" + cmd + " \"";
			// #endif

			p = CreateProcess(cmdFile, workDirectory, environ);
			ShellRequest req = new ShellRequest(cmd, p, quiet);

			ThreadPool.QueueUserWorkItem(delegate (object state)
			{
				try
				{
					do
					{

#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
						string line = p.StandardOutput.ReadLine(); //TODO: Split line from unicode stream
#else
						string line = p.StandardOutput.ReadLine();
#endif

						if (line != null)
						{
							_queue.Enqueue((req, LogEventType.InfoLog, line));
						}

					} while (!p.StandardOutput.EndOfStream);

					do
					{
						string error = p.StandardError.ReadLine();

						if (!string.IsNullOrEmpty(error))
						{
							if (!string.IsNullOrEmpty(error))
								_queue.Enqueue((req, LogEventType.ErrorLog, error));
						}
					} while (!p.StandardError.EndOfStream);

					_queue.Enqueue((req, LogEventType.EndStream, p.ExitCode));

					p.Close();
					p = null;
				}
				catch (Exception e)
				{
					UnityEngine.Debug.LogException(new("shell execute fail", e));
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

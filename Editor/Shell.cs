// #define DETECT_STDOUT_ENCODING

using System.Diagnostics;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

namespace com.bbbirder.unityeditor
{
	public enum LogType
	{
		Info,
		Warn,
		Error,
	}

	public static class Shell
	{
		/// <summary>
		/// Whether to throw when a shell returns non-zero exit code.
		/// </summary>
		public static bool ThrowOnNonZeroExitCode = false;

		private volatile static List<(ShellRequest, LogType, string)> _queue = new();
		public static Dictionary<string, string> DefaultEnvironment = new();


		static Shell()
		{
			_queue ??= new();
			EditorApplication.update += DumpQueue;
		}

		private static void DumpQueue()
		{
			lock (_queue)
			{
				for (int i = 0; i < _queue.Count; i++)
				{
					try
					{
						var (req, type, log) = _queue[i];
						req?.Log(type, log);
					}
					catch (Exception e)
					{
						UnityEngine.Debug.LogException(e);
					}
				}
				_queue.Clear();
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

		/// <summary>
		/// Run a command
		/// </summary>
		/// <param name="cmd"></param>
		/// <param name="workDirectory"></param>
		/// <param name="environmentVars"></param>
		/// <returns></returns>
		public static ShellRequest RunCommand(string cmd, string workDirectory = ".", Dictionary<string, string> environmentVars = null)
		{
			ShellRequest req = new ShellRequest(cmd);
			System.Threading.ThreadPool.QueueUserWorkItem(delegate (object state)
			{
				Process p = null;
				try
				{
					var shellApp =
#if UNITY_EDITOR_WIN
						"cmd.exe";
#elif UNITY_EDITOR_OSX
						"bash";
#endif
					ProcessStartInfo start = new ProcessStartInfo(shellApp);
			ApplyEnviron(start, DefaultEnvironment);
			ApplyEnviron(start, environ);
#if UNITY_EDITOR_WIN
#if DETECT_STDOUT_ENCODING
					start.Arguments = "/u /c \"chcp 65001>nul&" + cmd + " \"";
#else
					start.Arguments = "/c \"" + cmd + " \"";
#endif
#else
					start.Arguments += "-c \"" + cmd + " \"";
#endif
					start.CreateNoWindow = true;
					start.ErrorDialog = true;
					start.UseShellExecute = false;
					start.WorkingDirectory = workDirectory;

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

					p = Process.Start(start);
					req.Process = p;

					do
					{
						if (p.HasExited) break;

#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
						string line = p.StandardOutput.ReadLine(); //TODO: Split line from unicode stream
#else
						string line = p.StandardOutput.ReadLine();
#endif

						if (line != null)
						{
							lock (_queue)
							{
								_queue.Add((req, LogType.Info, line));
							}
						}

					} while (true);

					do
					{

						if (p.HasExited) break;
						string error = p.StandardError.ReadLine();

						if (!string.IsNullOrEmpty(error))
						{
							lock (_queue)
							{
								if (!string.IsNullOrEmpty(error))
									_queue.Add((req, LogType.Error, error));
							}
						}
					} while (true);

					req.NotifyComplete(p.ExitCode);
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

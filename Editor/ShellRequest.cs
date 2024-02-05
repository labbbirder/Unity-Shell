using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace com.bbbirder.unityeditor
{

    public class ShellRequest : INotifyCompletion
    {
        public event Action<LogType, string> onLog;
        public event Action<int> onComplete;
        internal Process Process { get; set; }
        internal ShellResult result;

        bool _completed = false;
        public bool IsCompleted => _completed;
        public void OnCompleted(Action continuation)
        {
            if (_completed)
            {
                continuation?.Invoke();
            }
            else
            {

                _completed = true;
                onComplete += _ => continuation?.Invoke();
            }
        }
        public ShellResult GetResult() => result;
        internal ShellRequest(string command)
        {
            result = new(command);
        }


        public void Log(LogType type, string log)
        {
            if (type == LogType.Info)
            {
                result.outputBuilder.AppendLine(log);
            }
            else if (type == LogType.Error)
            {
                result.errorBuilder.AppendLine(log);
            }

            if (onLog != null)
            {
                onLog(type, log);
            }
            else
            {
#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
					var bytes = Encoding.Unicode.GetBytes(log);
					if (ConsoleUtils.IsUTF8InsteadOf16(bytes))
					{
						log = Encoding.UTF8.GetString(bytes);
					}
#endif
                log = ConsoleUtils.ConvertToUnityColor(log);
                if (type == LogEventType.InfoLog)
                {
                    foreach (var l in log.Split("\n"))
                        UnityEngine.Debug.Log("<color=#808080>[ Shell Output ]</color>" + l);
                }
                else if (type == LogType.Error)
                {
                    foreach (var l in log.Split("\n"))
                        UnityEngine.Debug.LogError("<color=#808080>[ Shell Output ]</color>" + l);
                }
            }
        }

        public ShellResult Wait()
        {
            Process.WaitForExit();
            NotifyComplete(Process.ExitCode);
            return result;
        }

        internal void NotifyComplete(int ExitCode)
        {
            result.NotifyComplete(ExitCode);
            if (ExitCode != 0 && Shell.ThrowOnNonZeroExitCode)
            {
                throw new($"shell exit with code {ExitCode}, {result.Error}");
            }
            onComplete?.Invoke(ExitCode);
            onComplete = null;
        }

        public ShellRequest GetAwaiter() => this;
    }

}
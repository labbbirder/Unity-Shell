using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace com.bbbirder.unityeditor
{

    public class ShellRequest : INotifyCompletion
    {
        public event Action<LogEventType, string> onLog;
        public event Action<int> onComplete;
        bool isQuiet;
        // Process process;
        ShellResult result;

        bool _completed = false;
        public bool IsCompleted => _completed;
        internal ShellRequest(string command, Process proc, bool quiet)
        {
            // process = proc;
            _completed = false;
            result = new(command);
            isQuiet = quiet;
        }
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
        internal ShellRequest(string command, Process proc)
        {
            process = proc;
            result = new(command);
        }


        public void Log(LogEventType type, string log)
        {
            result.AppendLine(type, log);

#if UNITY_EDITOR_WIN && DETECT_STDOUT_ENCODING
            var bytes = Encoding.Unicode.GetBytes(log);
            if (ConsoleUtils.IsUTF8InsteadOf16(bytes))
            {
                log = Encoding.UTF8.GetString(bytes);
            }
#endif

            onLog?.Invoke(type, log);

            if (!isQuiet)
            {

                log = ConsoleUtils.ConvertToUnityColor(log);
                if (type == LogEventType.InfoLog)
                {
                    foreach (var l in log.Split("\n"))
                        UnityEngine.Debug.Log("<color=#808080>[ Shell Output ]</color>" + l);
                }
                else if (type == LogEventType.ErrorLog)
                {
                    foreach (var l in log.Split("\n"))
                        UnityEngine.Debug.LogError("<color=#808080>[ Shell Output ]</color>" + l);
                }
            }
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
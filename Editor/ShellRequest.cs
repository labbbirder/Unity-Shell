using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;

namespace com.bbbirder.unityeditor
{

    public class ShellRequest : INotifyCompletion
    {
        public event Action<LogEventType, string> onLog;
        public event Action<int> onComplete;
        bool isQuiet;
        bool isThrowOnNonZeroExitCode;
        // Process process;
        ShellResult result;
        string m_pendingOutput;
        internal string PendingOutput
        {
            get
            {
                return m_pendingOutput;
            }
            set
            {
                if (ReferenceEquals(m_pendingOutput, value)) return;
                pseudoProgress += (1 - pseudoProgress) * 0.1f;
                Progress.Report(progressId, pseudoProgress, m_pendingOutput = value);
            }
        }
        float pseudoProgress;
        int progressId;
        bool m_IsCompleted;
        public bool IsCompleted => m_IsCompleted;
        public ShellResult GetResult() => result;
        internal ShellRequest(string command, Process proc, bool quiet, bool throwOnNonZeroExitCode)
        {
            // process = proc;
            m_IsCompleted = false;
            result = new(command);
            isQuiet = quiet;
            isThrowOnNonZeroExitCode = throwOnNonZeroExitCode;
            progressId = -1;
            progressId = Progress.Start(command, command);

            Progress.RegisterPauseCallback(progressId, (isPause) =>
            {
                PromptWindow.Show(PendingOutput, input =>
                {
                    proc.StandardInput.WriteLine(input);
                    Shell.queue.Enqueue((this, LogEventType.InfoLog, input + "\n"));
                });
                return false;
            });
            Progress.RegisterCancelCallback(progressId, () =>
            {
                try
                {
                    if (!proc.CloseMainWindow())
                    {
                        proc.Kill();
                    }
                    proc.Dispose();
                    proc = null;
                }
                catch
                {
                }
                return proc is null || proc.HasExited;
            });
        }

        public void OnCompleted(Action continuation)
        {
            if (IsCompleted)
            {
                continuation?.Invoke();
            }
            else
            {
                onComplete += _ => continuation?.Invoke();
            }
        }


        public IEnumerator ToCoroutine()
        {
            while (!IsCompleted)
            {
                Shell.DumpQueue();
                yield return null;
            }
        }

        public ShellResult Wait()
        {
            while (!IsCompleted)
            {
                Shell.DumpQueue();
                Task.Delay(10).Wait();
            }
            return result;
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
                else if (type == LogEventType.EndStream)
                {
                    if (result.ExitCode != 0)
                    {
                        UnityEngine.Debug.LogError("<color=#808080>[ Shell Output ]</color>" + $"{result.Command} exit with code {result.ExitCode}");
                    }
                }
            }
        }


        internal void NotifyComplete(int ExitCode)
        {
            if (progressId != -1)
            {
                Progress.Remove(progressId);
            }
            result.NotifyComplete(ExitCode);
            if (ExitCode != 0 && isThrowOnNonZeroExitCode)
            {
                throw new($"shell exit with code {ExitCode}, {result.Error}");
            }
            onComplete?.Invoke(ExitCode);
            onComplete = null;
            m_IsCompleted = true;
        }

        public ShellRequest GetAwaiter() => this;
    }

}
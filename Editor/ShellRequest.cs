using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace com.bbbirder.unityeditor
{

    public class ShellRequest : INotifyCompletion
    {
        public event Action<LogEventType, string> onLog;
        public event Action<int> onComplete;
        string command;
        ShellSettings settings;
        Process process;
        ShellResult result;
        string m_pendingOutput;
        StringBuilder lineBuilder;
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
                UpdateProgressBar(command, value, pseudoProgress);
            }
        }
        float pseudoProgress;
        int progressId;
        bool m_IsCompleted;
        public bool IsCompleted => m_IsCompleted;
        public ShellResult GetResult() => result;
        internal ShellRequest(string command, ShellSettings settings, Process proc)
        {
            process = proc;
            this.lineBuilder = new();
            this.command = command;
            this.settings = settings;
            m_IsCompleted = false;
            result = new(command);

            UpdateProgressBar("Shell", command, 0);
            progressId = -1;
            progressId = Progress.Start(command, command);

            Progress.RegisterPauseCallback(progressId, (isPause) =>
            {
                PromptWindow.Show(PendingOutput, Input);
                return false;
            });
            Progress.RegisterCancelCallback(progressId, () =>
            {
                return TryCancel();
            });
        }

        void UpdateProgressBar(string title, string message, float progress)
        {
            if (!settings.withProgress) return;
            if (settings.progressCancelable)
            {
                if (EditorUtility.DisplayCancelableProgressBar(title, message, progress))
                {
                    TryCancel();
                }
            }
            else
            {
                EditorUtility.DisplayProgressBar(title, message, progress);
            }
        }

        /// <summary>
        /// Write user input to the process
        /// </summary>
        /// <param name="input"></param>
        public void Input(string input)
        {
            process.StandardInput.WriteLine(input);
            var line = input.EndsWith('\n') ? input : input + '\n';
            OnReceiveData(LogEventType.InfoLog, line);
        }

        /// <summary>
        /// Try to cancel the process
        /// </summary>
        /// <returns></returns>
        public bool TryCancel()
        {
            if (process is null) return false;
            try
            {
                if (!process.CloseMainWindow())
                {
                    process.Kill();
                }
                process.Dispose();
                process = null;
                return process is null || process.HasExited;
            }
            catch
            {
                return true;
            }
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


        void OnLog(LogEventType type, string log)
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

            if (!settings.quiet)
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


        internal void OnReceiveData(LogEventType type, object raw)
        {
            if (type == LogEventType.EndStream)
            {
                if (lineBuilder.Length > 0)
                {
                    OnLog(LogEventType.InfoLog, lineBuilder.ToString());
                    lineBuilder.Clear();
                }
                NotifyComplete((int)raw);
            }
            else if (type == LogEventType.InfoLog)
            {
                var buf = (raw as string).AsSpan();
                var nIdx = ~0;
                var pendingOutput = "";
                while ((nIdx = buf.IndexOf('\n')) != ~0)
                {
                    lineBuilder.Append(buf.Slice(0, nIdx));
                    buf = buf.Slice(nIdx + 1);
                    pendingOutput = lineBuilder.ToString();
                    OnLog(type, pendingOutput);
                    lineBuilder.Clear();
                }
                lineBuilder.Append(buf);
                if (lineBuilder.Length > 0)
                {
                    pendingOutput = lineBuilder.ToString();
                }
                PendingOutput = pendingOutput;
            }
            else
            {
                OnLog(type, raw as string);
            }
        }


        void NotifyComplete(int ExitCode)
        {
            if (progressId != -1)
            {
                Progress.Remove(progressId);
            }
            if (settings.autoClearProgress && settings.withProgress)
            {
                EditorUtility.ClearProgressBar();
            }

            result.NotifyComplete(ExitCode);
            if (ExitCode != 0 && settings.throwOnNonZeroExitCode)
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
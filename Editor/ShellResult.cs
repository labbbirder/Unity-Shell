
using System.Text;

namespace com.bbbirder.unityeditor
{
    public class ShellResult
    {
        string m_Output;
        string m_Error;
        internal StringBuilder outputBuilder;
        internal StringBuilder errorBuilder;
        public string Output => m_Output ??= outputBuilder.ToString();
        public string Error => m_Error ??= errorBuilder.ToString();
        public string Command { get; private set; }
        public int ExitCode { get; private set; }

        internal ShellResult(string cmd)
        {
            Command = cmd;
            outputBuilder =
            errorBuilder = new();
            m_Output =
            m_Error = null;
        }

        internal void NotifyComplete(int exitCode)
        {
            ExitCode = exitCode;
            if (!string.IsNullOrEmpty(Error))
            {
                UnityEngine.Debug.LogError($"cmd: {Command} error:\n{Error}");
            }
        }
        internal void AppendLine(LogEventType type, string line)
        {
            if (type == LogEventType.InfoLog)
            {
                outputBuilder.AppendLine(line);
                m_Output = null;
            }
            if (type == LogEventType.ErrorLog)
            {
                errorBuilder.AppendLine(line);
                m_Error = null;
            }
        }
    }
}
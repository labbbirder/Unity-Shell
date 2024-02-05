
using System.Text;

namespace com.bbbirder.unityeditor
{
    public class ShellResult
    {
        public string Command { get; private set; }
        public int ExitCode { get; private set; }
        public string Output { get; private set; }
        public string Error { get; private set; }
        internal StringBuilder outputBuilder;
        internal StringBuilder errorBuilder;

        internal ShellResult(string cmd)
        {
            Command = cmd;
            outputBuilder =
            errorBuilder = new();
            Output =
            Error = "";
        }

        internal void NotifyComplete(int exitCode)
        {
            Output = outputBuilder.ToString();
            Error = errorBuilder.ToString();
            ExitCode = exitCode;
        }
    }
}
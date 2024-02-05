using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace com.bbbirder.unityeditor
{
    public static class ConsoleUtils
    {
        public const char PATH_SPLITTER =
#if UNITY_EDITOR_WIN
            ';'
#elif UNITY_EDITOR_OSX
			':'
#endif
            ;
        static Dictionary<int, string> foreColor = new(){
            {30, "#000000"}, //black
			{31, "#FF0000"}, //red
			{32, "#00FF00"}, //green
			{33, "#FFFF00"}, //yellow
			{34, "#0000FF"}, //blue
			{35, "#FF00FF"}, //magenta
			{36, "#00FFFF"}, //cyan
			{37, "#FFFFFF"}, //white
		};

        /// <summary>
        /// Parse standard color log to unity color log
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string NormalizeColor(string input)
        {
            var pattern = "\x1b" + @"\[(\d+;)?(\d+;)?(\d+)m";
            return Regex.Replace(input, pattern, m =>
            {
                foreach (Capture g in m.Groups)
                {
                    if (int.TryParse(g.Value, out var c))
                    {
                        if (c is 0 or 39) return "</color>";
                        if (foreColor.TryGetValue(c, out var col)) return $"<color={col}>";
                    }
                }
                return m.Value;
            });
        }

        /// <summary>
        /// When standard output encoding is not normalized, use this to guess the encoding on the fly.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public unsafe static bool IsUTF8InsteadOf16(byte[] bytes)
        {
            if (bytes.Length % 2 != 0) return true; // distinguish from utf-16 only
            if (!bytes.Any(b => b == 0))
            {
                _ = 0;
            }
            var c = 0;
            foreach (var b in bytes)
            {
                if (c != 0)
                {
                    c--;
                    if ((b & 0xc0) != 0x80)
                        return false;
                }
                else
                {
                    if (b == 0)
                        return false;
                    float f = 0xff ^ b;
                    int zcnt = (int)((*(uint*)&f << 1 >> 24) - 127);
                    c = stackalloc[]{
                        -1,-1,-1,3,2,1,-1,0,
                    }[zcnt];
                    if (c == -1)
                        return false;
                }
            }

            return c == 0;
        }
    }

}
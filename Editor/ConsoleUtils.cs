using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace com.bbbirder.unityeditor
{
    using static ConsoleUtils.ConsoleForeColor;
    public static class ConsoleUtils
    {
        public enum ConsoleForeColor
        {
            Default = 0,
            Black = 30,
            Red = 31,
            Green = 32,
            Yellow = 33,
            Blue = 34,
            Magenta = 35,
            Cyan = 36,
            White = 37,
        }
        public delegate string ColorMarkVisitor(ConsoleForeColor foreColor);
        public const char PATH_SPLITTER =
#if UNITY_EDITOR_WIN
            ';'
#elif UNITY_EDITOR_OSX
			':'
#endif
            ;
        internal static readonly char[] ANY_PATH_SPLITTER = new[]
        {
            ';',':'
        };

        /// <summary>
        /// Parse standard color log to unity color log
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string ConvertToUnityColor(string input)
            => ScanColorLog(input, DefaultColorMarkVisitor);

        /// <summary>
        /// Remove color marks
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string ConvertToNoColor(string input)
            => ScanColorLog(input, RemoveColorsVisitor);

        public static string ScanColorLog(string input, ColorMarkVisitor visitor)
        {

            var pattern = "\x1b" + @"\[(\d+;)?(\d+;)?(\d+)m";
            return Regex.Replace(input, pattern, m =>
            {
                foreach (Capture g in m.Groups)
                {
                    if (int.TryParse(g.Value, out var c))
                    {
                        return visitor(c is 0 or 39 ? Default : (ConsoleForeColor)c);
                    }
                }
                return m.Value;
            });
        }

        public static string DefaultColorMarkVisitor(ConsoleForeColor foreColor) => foreColor switch
        {
            Black => "<color=#000000>",
            Red => "<color=#FF0000>",
            Green => "<color=#00FF00>",
            Yellow => "<color=#FFFF00>",
            Blue => "<color=#0000FF>",
            Magenta => "<color=#FF00FF>",
            Cyan => "<color=#00FFFF>",
            White => "<color=#FFFFFF>",
            Default => "</color>",
            _ => ""
        };

        static string RemoveColorsVisitor(ConsoleForeColor foreColor) => "";

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
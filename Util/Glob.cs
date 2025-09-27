using System;
using System.Text.RegularExpressions;

namespace UniversalDeployer.Util
{
    public static class Glob
    {
        public static bool IsMatch(string pattern, string text, bool ignoreCase = true)
        {
            var regex = "^" + Regex.Escape(pattern)
                .Replace(@"\*\*", "___ANYDIR___")
                .Replace(@"\*", "___ANY___")
                .Replace(@"\?", "___ONE___")
                + "$";
            regex = regex.Replace("___ANYDIR___", ".*");
            regex = regex.Replace("___ANY___", "[^\\\\/]*");
            regex = regex.Replace("___ONE___", ".");
            var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            return Regex.IsMatch(text.Replace('/', '\\'), regex, opts);
        }
    }
}

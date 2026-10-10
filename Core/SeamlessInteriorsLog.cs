using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SeamlessInteriors
{
    // ─── THE MOD'S LOG, IN THE READER'S LANGUAGE ───
    //
    // The mod logs in Turkish, the author's language, from more than four hundred call sites. A
    // player or contributor who does not read Turkish gets a log they cannot use (owner,
    // 2026-09-18, chasing frame hitches by the log lines beside them: "I have no idea what they
    // mean because they're not in English").
    //
    // THIS CLASS IS NAMED MelonLogger ON PURPOSE. Every file of the mod is in this namespace, and
    // C# finds a type in the enclosing namespace before one brought in by a using directive, so
    // every existing MelonLogger.Msg, Warning and Error call in the mod now lands here without one
    // of those call sites being touched.
    //
    // WHY NOT TRANSLATE FROM OUTSIDE. It was tried on 2026-09-13 and 14: MelonLoader's logger
    // cannot be rewritten by a Harmony patch, and its callback handlers can observe a line but not
    // change it. Inside the mod there is nothing to intercept; the line simply has not been
    // printed yet.
    //
    // THE AUTHOR'S LINES are translated by a table, LogTranslations.tsv, embedded in the assembly:
    // tag, a pattern matching the Turkish line with its values captured, and the English line with
    // the values put back. It was generated from the source's own format strings and checked
    // against a full play session on 2026-09-18: every line of the author's was translated. A
    // line with no rule is printed as written, never dropped and never guessed at.
    //
    // NEW LINES carry both languages at the call site, Msg(turkish, english), and need no rule.
    //
    // TURKISH STAYS THE DEFAULT FOR A TURKISH MACHINE: the language follows the computer's display
    // language unless the LogLanguage preference says "tr" or "en".
    internal static class MelonLogger
    {
        // ITS OWN NAMED LOGGER, NOT MelonLoader's STATIC ONE. The static Msg works out which mod is
        // speaking by walking the call stack, and it does not always find one: lines logged from a
        // settings callback or from some Harmony patches came out with no [SeamlessInteriors] in
        // front of them (owner, 2026-09-18: "a couple of log calls do not have the seamless
        // interiors prefix ... the ones at launch seem to just say [SETTINGS]"). Seen the same day
        // in one tick of one method: the line that dressed a building had the prefix and the line
        // that opened its room did not. A named instance carries the name with it.
        private static readonly MelonLoader.MelonLogger.Instance s_Log = new MelonLoader.MelonLogger.Instance("SeamlessInteriors");

        // Above zero, informational lines are not written; warnings and errors always are. Raised only
        // around a window being refitted while a lever is dragged, which otherwise logs its setup lines
        // at every step of the slider. Nothing in the mod raises it.
        internal static int Quiet;

        public static void Msg(string txt) { if (Quiet > 0) return; s_Log.Msg(SeamlessInteriorsLog.Translate(txt)); }
        public static void Msg(object obj) { if (Quiet > 0) return; s_Log.Msg(SeamlessInteriorsLog.Translate(obj != null ? obj.ToString() : "null")); }
        public static void Warning(string txt) { s_Log.Warning(SeamlessInteriorsLog.Translate(txt)); }
        public static void Error(string txt) { s_Log.Error(SeamlessInteriorsLog.Translate(txt)); }
        public static void Error(string txt, Exception ex) { s_Log.Error(SeamlessInteriorsLog.Translate(txt), ex); }

        // Both languages given where the line is written. No table, no pattern.
        public static void Msg(string turkish, string english) { if (Quiet > 0) return; s_Log.Msg(SeamlessInteriorsLog.English ? english : turkish); }
        public static void Warning(string turkish, string english) { s_Log.Warning(SeamlessInteriorsLog.English ? english : turkish); }

        // For the table's own failure report, which must not come back through Translate.
        internal static void Raw(string txt) { s_Log.Warning(txt); }
    }

    internal static class SeamlessInteriorsLog
    {
        // "auto", "tr" or "en". Created with the mod's other preferences; until then, and whenever
        // it holds anything else, the computer decides.
        internal static MelonLoader.MelonPreferences_Entry<string> s_PrefLogLanguage;

        private static bool s_MachineChecked;
        private static bool s_MachineIsTurkish;

        internal static bool English
        {
            get
            {
                string wanted = s_PrefLogLanguage != null ? s_PrefLogLanguage.Value : null;
                if (!string.IsNullOrEmpty(wanted))
                {
                    wanted = wanted.Trim().ToLowerInvariant();
                    if (wanted == "tr") return false;
                    if (wanted == "en") return true;
                }

                if (!s_MachineChecked)
                {
                    s_MachineChecked = true;
                    try
                    {
                        // The display language, then the language Windows was installed in. Either
                        // being Turkish is enough: this is the author's own mod on the author's
                        // own machine, and that case must not change.
                        s_MachineIsTurkish =
                            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr"
                            || CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "tr"
                            || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "tr";
                    }
                    catch { s_MachineIsTurkish = false; }
                }
                return !s_MachineIsTurkish;
            }
        }

        private sealed class Rule { public string Pattern; public string Replacement; public Regex Compiled; }

        private static Dictionary<string, List<Rule>> s_RulesByTag;
        private static bool s_Broken;

        internal static string Translate(string txt)
        {
            if (s_Broken || string.IsNullOrEmpty(txt) || !English) return txt;

            try
            {
                if (s_RulesByTag == null) LoadRules();

                // Only the few rules filed under this line's own [TAG] are tried.
                string tag = TagOf(txt);
                List<Rule> rules;
                if (tag == null || !s_RulesByTag.TryGetValue(tag, out rules)) return txt;

                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule.Compiled == null)
                        rule.Compiled = new Regex(rule.Pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    if (rule.Compiled.IsMatch(txt)) return rule.Compiled.Replace(txt, rule.Replacement, 1);
                }
                return txt;
            }
            catch (Exception ex)
            {
                // A log line must never cost the mod anything. One failure and the table is left
                // alone for the rest of the session; the flag goes up before anything is printed.
                s_Broken = true;
                try { MelonLogger.Raw("[LOG] Translation table switched off: " + ex.Message); } catch { }
                return txt;
            }
        }

        private static string TagOf(string s)
        {
            int open = s.IndexOf('[');
            if (open < 0 || open > 12) return null;
            int close = s.IndexOf(']', open + 1);
            if (close < 0) return null;
            string tag = s.Substring(open + 1, close - open - 1);
            int colon = tag.IndexOf(':');
            return colon >= 0 ? tag.Substring(0, colon) : tag;
        }

        private static void LoadRules()
        {
            var table = new Dictionary<string, List<Rule>>();
            Assembly assembly = typeof(SeamlessInteriorsLog).Assembly;

            string resource = null;
            foreach (string name in assembly.GetManifestResourceNames())
                if (name.EndsWith("LogTranslations.tsv", StringComparison.OrdinalIgnoreCase)) { resource = name; break; }

            if (resource != null)
            {
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        string[] parts = line.Split('\t');
                        if (parts.Length != 3) continue;

                        List<Rule> list;
                        if (!table.TryGetValue(parts[0], out list)) { list = new List<Rule>(); table[parts[0]] = list; }
                        list.Add(new Rule { Pattern = parts[1], Replacement = parts[2] });
                    }
                }
            }

            s_RulesByTag = table;
        }

        // How many rules loaded, for the one line the mod prints about it at startup.
        internal static int RuleCount
        {
            get
            {
                try { if (s_RulesByTag == null) LoadRules(); } catch { return 0; }
                int count = 0;
                foreach (var pair in s_RulesByTag) count += pair.Value.Count;
                return count;
            }
        }
    }
}

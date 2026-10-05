using System.Globalization;
using UnityEngine;

namespace UdonSharp
{
    // ponytail: flat Smart placeholders; reject unsupported nested formatters during baking.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class LCGSmartFormatter : UdonSharpBehaviour
    {
        public string Format(string template, string[] names, string[] values, string language)
        {
            if (string.IsNullOrEmpty(template)) return template ?? "";
            string result = "";
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
                { result += c; i++; continue; }
                if (c != '{') { result += c; continue; }
                int start = i, depth = 1;
                while (++i < template.Length)
                { if (template[i] == '{') depth++; if (template[i] == '}' && --depth == 0) break; }
                if (i >= template.Length) return template;
                string token = template.Substring(start + 1, i - start - 1);
                int colon = token.IndexOf(':');
                string name = colon < 0 ? token : token.Substring(0, colon);
                string value = null;
                if (names != null && values != null)
                    for (int j = 0; j < names.Length && j < values.Length; j++)
                        if (names[j] == name) { value = values[j] ?? ""; break; }
                if (value == null) { result += template.Substring(start, i - start + 1); continue; }
                if (colon < 0) { result += value; continue; }
                string format = token.Substring(colon + 1);
                if (format.StartsWith("choose("))
                {
                    int end = format.IndexOf("):");
                    if (end < 0) { result += value; continue; }
                    string[] choices = format.Substring(7, end - 7).Split('|');
                    string[] outputs = format.Substring(end + 2).Split('|');
                    int choice = choices.Length;
                    for (int j = 0; j < choices.Length; j++) if (value == choices[j]) { choice = j; break; }
                    result += choice < outputs.Length ? outputs[choice].Replace("{}", value) : "";
                }
                else if (format.StartsWith("plural:") || format.StartsWith("p:"))
                {
                    string[] outputs = format.Substring(format.IndexOf(':') + 1).Split('|');
                    double number;
                    bool numeric = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                    // Bake accepts one-form locales and English two-form plurals only.
                    int choice = language == "ja" || language == "th" || language == "zh" ? 0 : (numeric && number == 1 ? 0 : 1);
                    result += choice < outputs.Length ? outputs[choice].Replace("{}", value) : "";
                }
                else
                {
                    double number;
                    result += IsNumericFormat(format) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                        ? number.ToString(format, CultureInfo.InvariantCulture) : value;
                }
            }
            return result;
        }

        private bool IsNumericFormat(string format)
        {
            if (string.IsNullOrEmpty(format)) return false;
            if (format[0] == '0')
            {
                for (int i = 0; i < format.Length; i++)
                    if (format[i] != '0' && format[i] != '#' && format[i] != '.' && format[i] != ',') return false;
                return true;
            }
            if (format.Length > 3 || "DFNPEdfnpe".IndexOf(format[0]) < 0) return false;
            // D is integer-only; values are parsed as doubles.
            if (format[0] == 'D' || format[0] == 'd') return false;
            for (int i = 1; i < format.Length; i++) if (format[i] < '0' || format[i] > '9') return false;
            return true;
        }
    }
}

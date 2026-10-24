using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

internal abstract class JNode { }

internal sealed class JStr : JNode { public readonly string S; public JStr(string s) { S = s ?? ""; } }
internal sealed class JNum : JNode { public readonly long V; public JNum(long v) { V = v; } }
internal sealed class JBool : JNode { public readonly bool V; public JBool(bool v) { V = v; } }

internal sealed class JArr : JNode
{
    public readonly List<JNode> Items = new List<JNode>();
    public JArr() { }
    public JArr(params JNode[] nodes) { Items.AddRange(nodes); }
}

internal sealed class JObj : JNode
{
    public readonly List<KeyValuePair<string, JNode>> Items = new List<KeyValuePair<string, JNode>>();
    public JNode this[string key] { set { Items.Add(new KeyValuePair<string, JNode>(key, value)); } }
}

internal static class Json
{
    public static string Value(JNode n)
    {
        var sb = new StringBuilder(256);
        Write(n, sb);
        return sb.ToString();
    }

    public static void Out(JNode n)
    {
        Console.Out.WriteLine(Value(n));
    }

    static void Write(JNode n, StringBuilder sb)
    {
        if (n == null) { sb.Append("null"); return; }
        if (n is JStr) { WriteString(((JStr)n).S, sb); return; }
        if (n is JNum) { sb.Append(((JNum)n).V.ToString(CultureInfo.InvariantCulture)); return; }
        if (n is JBool) { sb.Append(((JBool)n).V ? "true" : "false"); return; }
        if (n is JArr)
        {
            var a = (JArr)n;
            sb.Append('[');
            for (int i = 0; i < a.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Write(a.Items[i], sb);
            }
            sb.Append(']');
            return;
        }
        if (n is JObj)
        {
            var o = (JObj)n;
            sb.Append('{');
            for (int i = 0; i < o.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(o.Items[i].Key, sb);
                sb.Append(':');
                Write(o.Items[i].Value, sb);
            }
            sb.Append('}');
            return;
        }
        sb.Append("null");
    }

    // JSON string escaping; orphan surrogates become U+FFFD (window titles can carry them).
    static void WriteString(string s, StringBuilder sb)
    {
        const char Q = '"';   // 0x22
        const char BS = '\\'; // 0x5C
        sb.Append(Q);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == Q) { sb.Append(BS).Append(Q); }
            else if (c == BS) { sb.Append(BS).Append(BS); }
            else if (c == '\u0008') { sb.Append(BS).Append('b'); }
            else if (c == '\u000C') { sb.Append(BS).Append('f'); }
            else if (c == '\u000A') { sb.Append(BS).Append('n'); }
            else if (c == '\u000D') { sb.Append(BS).Append('r'); }
            else if (c == '\u0009') { sb.Append(BS).Append('t'); }
            else if (c < 0x20) sb.Append(BS).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else if (char.IsHighSurrogate(c))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                else sb.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(c)) sb.Append('\uFFFD');
            else sb.Append(c);
        }
        sb.Append(Q);
    }
}

using System.Text;

namespace Punk.Steam;

/// <summary>A node of a Valve KeyValues (VDF/ACF) document: either a string value or a set of children.</summary>
public sealed class VdfNode
{
    public string? Value { get; init; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;

    public long GetLong(string key) => long.TryParse(GetString(key), out long v) ? v : 0;
}

/// <summary>Minimal parser for Valve's text KeyValues format.</summary>
public static class Vdf
{
    public static VdfNode ParseFile(string path) => Parse(File.ReadAllText(path));

    public static VdfNode Parse(string text)
    {
        int pos = 0;
        VdfNode root = new();
        ParseBlock(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseBlock(string s, ref int pos, VdfNode into, bool topLevel)
    {
        while (true)
        {
            string? key = NextToken(s, ref pos, out bool isBrace);
            if (key is null)
            {
                if (topLevel) return;
                throw new FormatException("Unexpected end of VDF data.");
            }

            if (isBrace)
            {
                if (key == "}" && !topLevel) return;
                throw new FormatException($"Unexpected '{key}' in VDF data.");
            }

            string? next = NextToken(s, ref pos, out bool nextIsBrace);
            if (next is null) throw new FormatException("Unexpected end of VDF data.");

            if (nextIsBrace && next == "{")
            {
                VdfNode child = new();
                ParseBlock(s, ref pos, child, topLevel: false);
                into.Children[key] = child;
            }
            else if (!nextIsBrace)
            {
                into.Children[key] = new VdfNode { Value = next };
            }
            else
            {
                throw new FormatException("Unexpected '}' in VDF data.");
            }
        }
    }

    // Returns the next quoted/unquoted string, or "{" / "}" with isBrace = true. Null at end of input.
    private static string? NextToken(string s, ref int pos, out bool isBrace)
    {
        isBrace = false;
        while (pos < s.Length)
        {
            char c = s[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }

        if (pos >= s.Length) return null;

        char ch = s[pos];
        if (ch is '{' or '}')
        {
            pos++;
            isBrace = true;
            return ch.ToString();
        }

        if (ch == '"')
        {
            pos++;
            StringBuilder sb = new();
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length)
                {
                    pos++;
                    sb.Append(s[pos] switch { 'n' => '\n', 't' => '\t', var other => other });
                }
                else
                {
                    sb.Append(s[pos]);
                }
                pos++;
            }
            pos++; // closing quote
            return sb.ToString();
        }

        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('{' or '}' or '"')) pos++;
        return s[start..pos];
    }
}

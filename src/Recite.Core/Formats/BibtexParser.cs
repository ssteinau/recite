using System.Text;

namespace Recite.Core.Formats;

/// <summary>A raw parsed bib entry: its type, cite-key, and fields (macros resolved).</summary>
public sealed class BibEntry
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string field) => Fields.TryGetValue(field, out var v) ? v : null;
}

/// <summary>
/// A hand-written BibTeX/biblatex parser. Handles <c>@string</c> macros, <c>#</c>
/// concatenation, quoted and braced values with nested braces, and <c>@comment</c>.
/// Values are returned brace-balanced and raw (LaTeX decoding is applied later so the
/// caller controls when it happens). PLAN §11: parser as a first-class Core component.
/// </summary>
public sealed class BibtexParser
{
    private readonly string _s;
    private int _i;
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public BibtexParser(string text) => _s = text ?? "";

    public List<BibEntry> Parse()
    {
        var entries = new List<BibEntry>();
        while (true)
        {
            if (!SkipToAt()) break;
            _i++; // consume '@'
            var type = ReadName().ToLowerInvariant();
            SkipWs();
            if (_i >= _s.Length) break;
            char open = _s[_i];
            if (open != '{' && open != '(') { continue; }
            char close = open == '{' ? '}' : ')';
            _i++; // consume opener

            if (type is "comment")
            {
                SkipBalanced(open, close);
                continue;
            }
            if (type is "preamble")
            {
                SkipBalanced(open, close);
                continue;
            }
            if (type is "string")
            {
                ReadStringMacro(close);
                continue;
            }

            var entry = ReadEntry(type, close);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private BibEntry? ReadEntry(string type, char close)
    {
        SkipWs();
        var key = ReadUntilAny(",", close.ToString()).Trim();
        var entry = new BibEntry { Type = type, Key = key };
        SkipWs();
        while (_i < _s.Length && _s[_i] != close)
        {
            if (_s[_i] == ',') { _i++; SkipWs(); continue; }
            var name = ReadName().ToLowerInvariant();
            SkipWs();
            if (_i < _s.Length && _s[_i] == '=')
            {
                _i++;
                SkipWs();
                var value = ReadValue();
                if (!string.IsNullOrEmpty(name))
                    entry.Fields[name] = value;
            }
            else
            {
                // malformed; bail out of this entry
                SkipBalancedFrom(close);
                break;
            }
            SkipWs();
        }
        if (_i < _s.Length && _s[_i] == close) _i++;
        return string.IsNullOrEmpty(entry.Type) ? null : entry;
    }

    private void ReadStringMacro(char close)
    {
        SkipWs();
        var name = ReadName();
        SkipWs();
        if (_i < _s.Length && _s[_i] == '=')
        {
            _i++;
            SkipWs();
            var value = ReadValue();
            if (!string.IsNullOrEmpty(name)) _strings[name] = value;
        }
        SkipWs();
        if (_i < _s.Length && _s[_i] == close) _i++;
    }

    /// <summary>Read a (possibly concatenated) field value and resolve @string macros.</summary>
    private string ReadValue()
    {
        var parts = new List<string>();
        while (true)
        {
            SkipWs();
            if (_i >= _s.Length) break;
            char c = _s[_i];
            if (c == '{') parts.Add(ReadBraced());
            else if (c == '"') parts.Add(ReadQuoted());
            else if (char.IsDigit(c)) parts.Add(ReadNumber());
            else if (IsNameStart(c))
            {
                var macro = ReadName();
                parts.Add(_strings.TryGetValue(macro, out var v) ? v : macro);
            }
            else break;

            SkipWs();
            if (_i < _s.Length && _s[_i] == '#') { _i++; continue; }
            break;
        }
        return string.Concat(parts);
    }

    private string ReadBraced()
    {
        // assumes _s[_i] == '{'
        var sb = new StringBuilder();
        int depth = 0;
        do
        {
            char c = _s[_i];
            if (c == '{') { if (depth > 0) sb.Append(c); depth++; }
            else if (c == '}') { depth--; if (depth > 0) sb.Append(c); }
            else sb.Append(c);
            _i++;
        } while (_i < _s.Length && depth > 0);
        return sb.ToString();
    }

    private string ReadQuoted()
    {
        _i++; // consume opening quote
        var sb = new StringBuilder();
        int depth = 0;
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (c == '{') depth++;
            else if (c == '}') depth = Math.Max(0, depth - 1);
            else if (c == '"' && depth == 0) { _i++; break; }
            sb.Append(c);
            _i++;
        }
        return sb.ToString();
    }

    private string ReadNumber()
    {
        int start = _i;
        while (_i < _s.Length && char.IsLetterOrDigit(_s[_i])) _i++;
        return _s[start.._i];
    }

    // ---- low-level scanning -------------------------------------------------

    private bool SkipToAt()
    {
        while (_i < _s.Length && _s[_i] != '@') _i++;
        return _i < _s.Length;
    }

    private void SkipWs()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private string ReadName()
    {
        SkipWs();
        int start = _i;
        while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || "-_:./+".IndexOf(_s[_i]) >= 0)) _i++;
        return _s[start.._i];
    }

    private string ReadUntilAny(string set1, string set2)
    {
        int start = _i;
        while (_i < _s.Length && set1.IndexOf(_s[_i]) < 0 && set2.IndexOf(_s[_i]) < 0) _i++;
        return _s[start.._i];
    }

    private void SkipBalanced(char open, char close)
    {
        int depth = 1;
        while (_i < _s.Length && depth > 0)
        {
            if (_s[_i] == open) depth++;
            else if (_s[_i] == close) depth--;
            _i++;
        }
    }

    private void SkipBalancedFrom(char close)
    {
        while (_i < _s.Length && _s[_i] != close) _i++;
    }
}

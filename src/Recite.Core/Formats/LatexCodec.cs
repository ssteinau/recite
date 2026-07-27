using System.Text;

namespace Recite.Core.Formats;

/// <summary>
/// Best-effort conversion between LaTeX accent/escape sequences and Unicode.
/// Not a full TeX engine — covers the accents and symbols that actually occur in
/// real-world <c>.bib</c> files (PLAN §11 mitigation: careful boundary, CSL-JSON internally).
/// </summary>
public static class LatexCodec
{
    // Explicit sequence table keeps things predictable and testable.
    private static readonly (string tex, string uni)[] Sequences =
    {
        // acute
        (@"\'a","á"),(@"\'e","é"),(@"\'i","í"),(@"\'o","ó"),(@"\'u","ú"),(@"\'y","ý"),
        (@"\'A","Á"),(@"\'E","É"),(@"\'I","Í"),(@"\'O","Ó"),(@"\'U","Ú"),
        (@"\'n","ń"),(@"\'c","ć"),(@"\'s","ś"),(@"\'z","ź"),(@"\'l","ĺ"),(@"\'r","ŕ"),
        // grave
        (@"\`a","à"),(@"\`e","è"),(@"\`i","ì"),(@"\`o","ò"),(@"\`u","ù"),
        (@"\`A","À"),(@"\`E","È"),(@"\`O","Ò"),(@"\`U","Ù"),
        // umlaut / diaeresis
        ("\\\"a","ä"),("\\\"e","ë"),("\\\"i","ï"),("\\\"o","ö"),("\\\"u","ü"),("\\\"y","ÿ"),
        ("\\\"A","Ä"),("\\\"E","Ë"),("\\\"O","Ö"),("\\\"U","Ü"),
        // circumflex
        (@"\^a","â"),(@"\^e","ê"),(@"\^i","î"),(@"\^o","ô"),(@"\^u","û"),
        (@"\^A","Â"),(@"\^E","Ê"),(@"\^O","Ô"),(@"\^U","Û"),
        // tilde
        (@"\~n","ñ"),(@"\~a","ã"),(@"\~o","õ"),(@"\~N","Ñ"),(@"\~A","Ã"),(@"\~O","Õ"),
        // caron / háček
        (@"\v c","č"),(@"\v s","š"),(@"\v z","ž"),(@"\v r","ř"),(@"\v e","ě"),(@"\v n","ň"),
        (@"\v C","Č"),(@"\v S","Š"),(@"\v Z","Ž"),
        // cedilla
        (@"\c c","ç"),(@"\c C","Ç"),(@"\c s","ş"),(@"\c S","Ş"),
        // ring
        (@"\r a","å"),(@"\r A","Å"),
        // special letters / symbols
        (@"\ss","ß"),(@"\o","ø"),(@"\O","Ø"),(@"\ae","æ"),(@"\AE","Æ"),
        (@"\aa","å"),(@"\AA","Å"),(@"\l","ł"),(@"\L","Ł"),
        (@"\&","&"),(@"\%","%"),(@"\$","$"),(@"\#","#"),(@"\_","_"),
        (@"\textendash","–"),(@"\textemdash","—"),(@"\ldots","…"),
        (@"---","—"),(@"--","–"),(@"~"," "),
    };

    public static string Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Handle brace-wrapped accent forms first: {\"o}, {\'e}, {\v{c}}, {\c{c}} → \"o …
        var s = input;
        // Normalise {\cmd{x}} → \cmd x  and {\cmd x} handled by sequence table.
        s = NormalizeAccentBraces(s);

        foreach (var (tex, uni) in Sequences)
            s = s.Replace(tex, uni);

        // Strip any remaining protective braces that are not escaped.
        s = StripBraces(s);
        return s.Trim();
    }

    private static string NormalizeAccentBraces(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            // {\cmd{x}} or {\cmd x}
            if (s[i] == '{' && i + 1 < s.Length && s[i + 1] == '\\')
            {
                int j = i + 2;
                var cmd = new StringBuilder("\\");
                while (j < s.Length && (char.IsLetter(s[j]) || "\"'`^~".IndexOf(s[j]) >= 0) && cmd.Length < 4)
                {
                    cmd.Append(s[j]);
                    // single-symbol accent commands (", ', `, ^, ~) take exactly one following char
                    if ("\"'`^~".IndexOf(s[j]) >= 0) { j++; break; }
                    j++;
                }
                // optional space
                if (j < s.Length && s[j] == ' ') j++;
                // argument: {x} or x
                string arg;
                if (j < s.Length && s[j] == '{')
                {
                    int k = j + 1;
                    var a = new StringBuilder();
                    while (k < s.Length && s[k] != '}') a.Append(s[k++]);
                    arg = a.ToString();
                    j = k + 1;
                }
                else if (j < s.Length)
                {
                    arg = s[j].ToString();
                    j++;
                }
                else arg = "";
                if (j < s.Length && s[j] == '}') j++;

                sb.Append(cmd).Append(cmd[^1] is '"' or '\'' or '`' or '^' or '~' ? "" : " ").Append(arg);
                i = j - 1;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string StripBraces(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (c != '{' && c != '}') sb.Append(c);
        return sb.ToString();
    }

    private static readonly (char uni, string tex)[] EncodeTable =
    {
        ('á',@"{\'a}"),('é',@"{\'e}"),('í',@"{\'i}"),('ó',@"{\'o}"),('ú',@"{\'u}"),
        ('à',@"{\`a}"),('è',@"{\`e}"),('ì',@"{\`i}"),('ò',@"{\`o}"),('ù',@"{\`u}"),
        ('ä',"{\\\"a}"),('ë',"{\\\"e}"),('ï',"{\\\"i}"),('ö',"{\\\"o}"),('ü',"{\\\"u}"),
        ('Ä',"{\\\"A}"),('Ö',"{\\\"O}"),('Ü',"{\\\"U}"),
        ('â',@"{\^a}"),('ê',@"{\^e}"),('î',@"{\^i}"),('ô',@"{\^o}"),('û',@"{\^u}"),
        ('ñ',@"{\~n}"),('ã',@"{\~a}"),('õ',@"{\~o}"),
        ('ç',@"{\c c}"),('Ç',@"{\c C}"),('č',@"{\v c}"),('š',@"{\v s}"),('ž',@"{\v z}"),
        ('å',@"{\aa}"),('Å',@"{\AA}"),('ø',@"{\o}"),('Ø',@"{\O}"),
        ('æ',@"{\ae}"),('Æ',@"{\AE}"),('ß',@"{\ss}"),('ł',@"{\l}"),('Ł',@"{\L}"),
    };

    /// <summary>Encode a value for a <c>.bib</c> field body (accents → TeX, escape specials).</summary>
    public static string Encode(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            var hit = EncodeTable.FirstOrDefault(e => e.uni == c);
            if (hit.tex is not null) { sb.Append(hit.tex); continue; }
            switch (c)
            {
                case '&': sb.Append(@"\&"); break;
                case '%': sb.Append(@"\%"); break;
                case '$': sb.Append(@"\$"); break;
                case '#': sb.Append(@"\#"); break;
                case '_': sb.Append(@"\_"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

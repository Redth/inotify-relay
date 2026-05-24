namespace InotifyRelay.Core.Templating;

// Grammar:
//   template := (literal | placeholder)*
//   placeholder := '{' name ('|' filter)* '}'
//   filter := name (':' arg)*
//   arg := single-quoted string  (e.g. 'foo' or 'with \\' escape')  OR bareword (digits / identifier)
//   '{{' and '}}' are literal braces.
internal static class TemplateParser
{
    public static Template Parse(string source)
    {
        var tokens = new List<TemplateToken>();
        var i = 0;
        var lit = new System.Text.StringBuilder();

        void FlushLiteral()
        {
            if (lit.Length > 0)
            {
                tokens.Add(new LiteralToken(lit.ToString()));
                lit.Clear();
            }
        }

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '{')
            {
                if (i + 1 < source.Length && source[i + 1] == '{') { lit.Append('{'); i += 2; continue; }
                FlushLiteral();
                i++;
                tokens.Add(ParsePlaceholder(source, ref i));
            }
            else if (c == '}')
            {
                if (i + 1 < source.Length && source[i + 1] == '}') { lit.Append('}'); i += 2; continue; }
                throw new TemplateException($"Unexpected '}}' at position {i}");
            }
            else
            {
                lit.Append(c);
                i++;
            }
        }
        FlushLiteral();
        return new Template(source, tokens);
    }

    private static VariableToken ParsePlaceholder(string s, ref int i)
    {
        var name = ReadIdent(s, ref i, allowDot: true);
        if (name.Length == 0)
            throw new TemplateException($"Empty placeholder at position {i}");

        var filters = new List<FilterCall>();
        SkipSpaces(s, ref i);
        while (i < s.Length && s[i] == '|')
        {
            i++;
            SkipSpaces(s, ref i);
            var fname = ReadIdent(s, ref i, allowDot: false);
            if (fname.Length == 0)
                throw new TemplateException($"Empty filter name at position {i}");
            var args = new List<string>();
            SkipSpaces(s, ref i);
            while (i < s.Length && s[i] == ':')
            {
                i++;
                SkipSpaces(s, ref i);
                args.Add(ReadArg(s, ref i));
                SkipSpaces(s, ref i);
            }
            filters.Add(new FilterCall(fname, args));
            SkipSpaces(s, ref i);
        }

        if (i >= s.Length || s[i] != '}')
            throw new TemplateException($"Unterminated placeholder, expected '}}' at position {i}");
        i++;
        return new VariableToken(name, filters);
    }

    private static void SkipSpaces(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
    }

    private static string ReadIdent(string s, ref int i, bool allowDot)
    {
        var start = i;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c) || c == '_' || (allowDot && c == '.'))
                i++;
            else
                break;
        }
        return s.Substring(start, i - start);
    }

    private static string ReadArg(string s, ref int i)
    {
        if (i >= s.Length)
            throw new TemplateException("Unexpected end of template while reading filter argument");

        if (s[i] == '\'')
        {
            // single-quoted string with \' and \\ escapes
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    var n = s[i + 1];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => n });
                    i += 2;
                }
                else if (c == '\'')
                {
                    i++;
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            throw new TemplateException("Unterminated string literal in filter argument");
        }

        // bareword (alnum/underscore/dot/dash)
        var start = i;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or '/') i++;
            else break;
        }
        if (start == i)
            throw new TemplateException($"Expected filter argument at position {i}");
        return s.Substring(start, i - start);
    }
}

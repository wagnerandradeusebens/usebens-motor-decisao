using System.Globalization;
using System.Text;

namespace MotorDecisao.Application.Formulas.Parsing;

/// <summary>
/// Turns a formula string into a stream of <see cref="Token"/>s.
///
/// pt-BR conventions:
/// <list type="bullet">
/// <item>Argument separator is <c>;</c>.</item>
/// <item>Because <c>;</c> is the separator, the decimal mark inside a bare number
/// literal is <c>.</c> (a comma would be ambiguous next to the separator). Numeric
/// <em>text</em> with a comma is still accepted via coercion.</item>
/// <item>Strings use double quotes; a doubled quote <c>""</c> is an escaped quote.</item>
/// <item>Booleans are the keywords <c>VERDADEIRO</c> / <c>FALSO</c>.</item>
/// <item>Identifiers (fields and function names) may contain letters, digits,
/// underscore and dots, e.g. <c>ARREDONDAR.PARA.CIMA</c> or <c>debt_ratio</c>.</item>
/// </list>
/// </summary>
public sealed class Lexer
{
    private readonly string _src;
    private int _pos;

    public Lexer(string source)
    {
        _src = source ?? string.Empty;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = Next();
            tokens.Add(token);
        }
        while (token.Type != TokenType.EndOfInput);

        return tokens;
    }

    private Token Next()
    {
        SkipWhitespace();

        if (_pos >= _src.Length)
        {
            return new Token(TokenType.EndOfInput, string.Empty, _pos);
        }

        var start = _pos;
        var c = _src[_pos];

        // Numbers: digits with an optional single '.' decimal part.
        if (char.IsDigit(c) || (c == '.' && start + 1 < _src.Length && char.IsDigit(_src[start + 1])))
        {
            return ReadNumber();
        }

        // Text literals use double quotes.
        if (c == '"')
        {
            return ReadString();
        }

        // Field references use single quotes: 'campo'.
        if (c == '\'')
        {
            return ReadField();
        }

        // Variable references use braces: {variavel}.
        if (c == '{')
        {
            return ReadVariable();
        }

        // Cross-policy references: $[Política;Categoria;Variável]. Lido inteiro
        // aqui (conteúdo cru até ']'), então o nome da política pode ter QUALQUER
        // caractere — parênteses, espaços, hífen — sem aspas e sem ambiguidade
        // com '(' de agrupamento.
        if (c == '$' && _pos + 1 < _src.Length && _src[_pos + 1] == '[')
        {
            return ReadPolicyRef();
        }

        // Identifiers / keywords.
        if (char.IsLetter(c) || c == '_')
        {
            return ReadIdentifierOrKeyword();
        }

        // Operators and punctuation.
        _pos++;
        switch (c)
        {
            case '+': return new Token(TokenType.Plus, "+", start);
            case '-': return new Token(TokenType.Minus, "-", start);
            case '*': return new Token(TokenType.Star, "*", start);
            case '/': return new Token(TokenType.Slash, "/", start);
            case '^': return new Token(TokenType.Caret, "^", start);
            case '&': return new Token(TokenType.Ampersand, "&", start);
            case '(': return new Token(TokenType.LeftParen, "(", start);
            case ')': return new Token(TokenType.RightParen, ")", start);
            case '[': return new Token(TokenType.LeftBracket, "[", start);
            case ']': return new Token(TokenType.RightBracket, "]", start);
            case ';': return new Token(TokenType.Separator, ";", start);
            case '=': return new Token(TokenType.Equal, "=", start);
            case '<':
                if (Peek() == '=') { _pos++; return new Token(TokenType.LessOrEqual, "<=", start); }
                if (Peek() == '>') { _pos++; return new Token(TokenType.NotEqual, "<>", start); }
                return new Token(TokenType.Less, "<", start);
            case '>':
                if (Peek() == '=') { _pos++; return new Token(TokenType.GreaterOrEqual, ">=", start); }
                return new Token(TokenType.Greater, ">", start);
            default:
                throw new FormulaException($"Caractere inesperado '{c}'.", start);
        }
    }

    private Token ReadNumber()
    {
        var start = _pos;
        var seenDot = false;
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (char.IsDigit(c))
            {
                _pos++;
            }
            else if (c == '.' && !seenDot)
            {
                seenDot = true;
                _pos++;
            }
            else
            {
                break;
            }
        }

        var lexeme = _src[start.._pos];
        if (!decimal.TryParse(lexeme, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormulaException($"Número inválido '{lexeme}'.", start);
        }

        return new Token(TokenType.Number, lexeme, start, Number: value);
    }

    private Token ReadString()
    {
        var start = _pos;
        _pos++; // opening quote
        var sb = new StringBuilder();

        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == '"')
            {
                // Doubled quote ("") is an escaped quote. Look at the *next*
                // character (_pos points at the current closing quote here).
                if (_pos + 1 < _src.Length && _src[_pos + 1] == '"')
                {
                    sb.Append('"');
                    _pos += 2;
                    continue;
                }

                _pos++; // closing quote
                return new Token(TokenType.String, sb.ToString(), start, Text: sb.ToString());
            }

            sb.Append(c);
            _pos++;
        }

        throw new FormulaException("Texto sem aspas de fechamento.", start);
    }

    /// <summary>Reads a single-quoted field reference <c>'campo'</c> (<c>''</c> escapes a quote).</summary>
    private Token ReadField()
    {
        var start = _pos;
        _pos++; // opening quote
        var sb = new StringBuilder();

        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == '\'')
            {
                if (_pos + 1 < _src.Length && _src[_pos + 1] == '\'')
                {
                    sb.Append('\'');
                    _pos += 2;
                    continue;
                }
                _pos++; // closing quote
                return new Token(TokenType.Field, sb.ToString(), start, Text: sb.ToString());
            }
            sb.Append(c);
            _pos++;
        }

        throw new FormulaException("Campo sem aspas simples de fechamento.", start);
    }

    /// <summary>Reads a brace-delimited variable reference <c>{variavel}</c>.</summary>
    private Token ReadVariable()
    {
        var start = _pos;
        _pos++; // opening brace
        var sb = new StringBuilder();

        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == '}')
            {
                _pos++; // closing brace
                var name = sb.ToString().Trim();
                if (name.Length == 0)
                {
                    throw new FormulaException("Referência de variável vazia: {}.", start);
                }
                return new Token(TokenType.Variable, name, start, Text: name);
            }
            sb.Append(c);
            _pos++;
        }

        throw new FormulaException("Variável sem chave de fechamento '}'.", start);
    }

    /// <summary>
    /// Reads a cross-policy reference <c>$[Política;Categoria;Variável]</c>. The
    /// content between <c>$[</c> and <c>]</c> is captured raw (unquoted), so a
    /// policy name may contain any character. The parser later splits it by ';'.
    /// </summary>
    private Token ReadPolicyRef()
    {
        var start = _pos;
        _pos += 2; // '$' and '['
        var sb = new StringBuilder();

        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (c == ']')
            {
                _pos++; // closing bracket
                var content = sb.ToString();
                if (content.Trim().Length == 0)
                {
                    throw new FormulaException("Referência de política vazia: $[].", start);
                }
                return new Token(TokenType.PolicyRef, content, start, Text: content);
            }
            sb.Append(c);
            _pos++;
        }

        throw new FormulaException("Referência de política sem ']' de fechamento.", start);
    }

    private Token ReadIdentifierOrKeyword()
    {
        var start = _pos;
        while (_pos < _src.Length)
        {
            var c = _src[_pos];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                _pos++;
            }
            else
            {
                break;
            }
        }

        var lexeme = _src[start.._pos];

        if (string.Equals(lexeme, "VERDADEIRO", StringComparison.OrdinalIgnoreCase))
        {
            return new Token(TokenType.Boolean, lexeme, start, Boolean: true);
        }
        if (string.Equals(lexeme, "FALSO", StringComparison.OrdinalIgnoreCase))
        {
            return new Token(TokenType.Boolean, lexeme, start, Boolean: false);
        }

        return new Token(TokenType.Identifier, lexeme, start);
    }

    private char Peek() => _pos < _src.Length ? _src[_pos] : '\0';

    private void SkipWhitespace()
    {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
        {
            _pos++;
        }
    }
}

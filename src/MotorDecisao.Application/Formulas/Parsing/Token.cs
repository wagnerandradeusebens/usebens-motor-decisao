namespace MotorDecisao.Application.Formulas.Parsing;

/// <summary>Lexical token categories for the formula language.</summary>
public enum TokenType
{
    Number,
    String,
    Boolean,
    Identifier,      // field reference or function name (function if followed by '(')
    Plus,
    Minus,
    Star,
    Slash,
    Caret,           // ^ power
    Ampersand,       // & concatenation
    Equal,           // =
    NotEqual,        // <>
    Less,            // <
    LessOrEqual,     // <=
    Greater,         // >
    GreaterOrEqual,  // >=
    LeftParen,
    RightParen,
    LeftBracket,     // '[' opens an external reference [Fonte;Produto;Dado]
    RightBracket,    // ']'
    Field,           // 'campo'  -> single-quoted request field reference
    Variable,        // {variavel} -> brace-delimited variable reference
    PolicyRef,       // $[Política;Categoria;Variável] -> cross-policy reference (conteúdo cru)
    Separator,       // ';' argument separator (pt-BR) / part separator inside [ ]
    EndOfInput
}

/// <summary>A single lexical token with its source position for error messages.</summary>
public readonly record struct Token(
    TokenType Type,
    string Lexeme,
    int Position,
    decimal Number = 0m,
    string? Text = null,
    bool Boolean = false);

/// <summary>
/// Raised on any lexing/parsing error, carrying a human-readable message (in
/// Portuguese, since business users author the formulas) and the source position.
/// </summary>
public sealed class FormulaException : Exception
{
    public int Position { get; }

    public FormulaException(string message, int position)
        : base(message)
    {
        Position = position;
    }
}

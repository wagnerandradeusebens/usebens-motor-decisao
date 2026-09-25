namespace MotorDecisao.Application.Formulas.Parsing;

/// <summary>
/// Recursive-descent parser turning a token stream into an AST. Precedence, from
/// lowest to highest:
/// <list type="number">
/// <item>comparison (= &lt;&gt; &lt; &lt;= &gt; &gt;=)</item>
/// <item>concatenation (&amp;)</item>
/// <item>additive (+ -)</item>
/// <item>multiplicative (* /)</item>
/// <item>power (^, right-associative)</item>
/// <item>unary (- +)</item>
/// <item>primary (literals, fields, functions, parentheses)</item>
/// </list>
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _index;

    private Parser(List<Token> tokens) => _tokens = tokens;

    /// <summary>Parses a whole formula string into an AST.</summary>
    public static FormulaNode Parse(string expression)
    {
        var tokens = new Lexer(expression).Tokenize();
        var parser = new Parser(tokens);
        var node = parser.ParseExpression();
        parser.Expect(TokenType.EndOfInput, "Expressão malformada: conteúdo extra ao final.");
        return node;
    }

    // --- Grammar ----------------------------------------------------------

    private FormulaNode ParseExpression() => ParseComparison();

    private FormulaNode ParseComparison()
    {
        var left = ParseConcat();
        while (true)
        {
            BinaryOperator? op = Current.Type switch
            {
                TokenType.Equal => BinaryOperator.Equal,
                TokenType.NotEqual => BinaryOperator.NotEqual,
                TokenType.Less => BinaryOperator.Less,
                TokenType.LessOrEqual => BinaryOperator.LessOrEqual,
                TokenType.Greater => BinaryOperator.Greater,
                TokenType.GreaterOrEqual => BinaryOperator.GreaterOrEqual,
                _ => null
            };
            if (op is null) break;
            Advance();
            var right = ParseConcat();
            left = new BinaryNode(op.Value, left, right);
        }
        return left;
    }

    private FormulaNode ParseConcat()
    {
        var left = ParseAdditive();
        while (Current.Type == TokenType.Ampersand)
        {
            Advance();
            var right = ParseAdditive();
            left = new BinaryNode(BinaryOperator.Concat, left, right);
        }
        return left;
    }

    private FormulaNode ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Type is TokenType.Plus or TokenType.Minus)
        {
            var op = Current.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
            Advance();
            var right = ParseMultiplicative();
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private FormulaNode ParseMultiplicative()
    {
        var left = ParsePower();
        while (Current.Type is TokenType.Star or TokenType.Slash)
        {
            var op = Current.Type == TokenType.Star ? BinaryOperator.Multiply : BinaryOperator.Divide;
            Advance();
            var right = ParsePower();
            left = new BinaryNode(op, left, right);
        }
        return left;
    }

    private FormulaNode ParsePower()
    {
        var left = ParseUnary();
        // Right-associative: 2^3^2 = 2^(3^2).
        if (Current.Type == TokenType.Caret)
        {
            Advance();
            var right = ParsePower();
            return new BinaryNode(BinaryOperator.Power, left, right);
        }
        return left;
    }

    private FormulaNode ParseUnary()
    {
        if (Current.Type == TokenType.Minus)
        {
            Advance();
            return new UnaryNode(UnaryOperator.Negate, ParseUnary());
        }
        if (Current.Type == TokenType.Plus)
        {
            Advance();
            return new UnaryNode(UnaryOperator.Plus, ParseUnary());
        }
        return ParsePrimary();
    }

    private FormulaNode ParsePrimary()
    {
        var token = Current;
        switch (token.Type)
        {
            case TokenType.Number:
                Advance();
                return new LiteralNode(FormulaValue.Number(token.Number));

            case TokenType.String:
                Advance();
                return new LiteralNode(FormulaValue.Text(token.Text ?? string.Empty));

            case TokenType.Boolean:
                Advance();
                return new LiteralNode(FormulaValue.Boolean(token.Boolean));

            case TokenType.LeftParen:
                // Desambigua referência de política de agrupamento: uma referência
                // tem a forma "( <parte> ; ..." — parte (ident/texto) seguida de ';'.
                // Agrupamento nunca usa ';', e chamada de função entra pelo case
                // Identifier (o '(' de função nunca cai aqui).
                if (IsPolicyRefAhead())
                {
                    return ParsePolicyRef();
                }
                Advance();
                var inner = ParseExpression();
                Expect(TokenType.RightParen, "Esperado ')'.");
                return inner;

            case TokenType.LeftBracket:
                return ParseExternalRef();

            case TokenType.Field:
                Advance();
                return new FieldNode(token.Text ?? string.Empty);

            case TokenType.Variable:
                Advance();
                return new VariableRefNode(token.Text ?? string.Empty);

            case TokenType.Identifier:
                Advance();
                // A function call if immediately followed by '('.
                if (Current.Type == TokenType.LeftParen)
                {
                    return ParseFunctionCall(token.Lexeme);
                }
                return new FieldNode(token.Lexeme);

            default:
                throw new FormulaException(
                    $"Token inesperado '{token.Lexeme}'.", token.Position);
        }
    }

    /// <summary>
    /// Parses an external reference <c>[Fonte;Produto;Dado]</c> — exactly three
    /// parts separated by ';'. Inside the brackets, ';' is a part separator (not
    /// an argument separator), because the brackets delimit the context.
    /// </summary>
    private FormulaNode ParseExternalRef()
    {
        var open = Current;
        Expect(TokenType.LeftBracket, "Esperado '['.");

        var parts = new List<string> { ReadRefPart() };
        while (Current.Type == TokenType.Separator)
        {
            Advance();
            parts.Add(ReadRefPart());
        }

        Expect(TokenType.RightBracket, "Esperado ']' fechando a referência externa.");

        if (parts.Count != 3)
        {
            throw new FormulaException(
                "Referência externa deve ter exatamente 3 partes: [Fonte;Produto;Dado].", open.Position);
        }

        return new ExternalRefNode(parts[0], parts[1], parts[2]);
    }

    /// <summary>
    /// Verdadeiro quando o que vem após '(' é uma referência de política — isto é,
    /// uma parte (identificador ou texto) imediatamente seguida de ';'. Distingue
    /// de agrupamento (nunca usa ';') sem consumir tokens.
    /// </summary>
    private bool IsPolicyRefAhead()
    {
        var first = PeekAt(1);
        var second = PeekAt(2);
        var firstIsPart = first.Type == TokenType.Identifier || first.Type == TokenType.String;
        return firstIsPart && second.Type == TokenType.Separator;
    }

    /// <summary>
    /// Parses a policy reference <c>(Política;Categoria;Variável)</c> — 2 ou 3
    /// partes. Categoria ∈ {Pontos, Limite, Resposta, Variaveis}. A 3ª parte
    /// (variável) só é exigida quando a categoria é "Variaveis".
    /// </summary>
    private FormulaNode ParsePolicyRef()
    {
        var open = Current;
        Expect(TokenType.LeftParen, "Esperado '('.");

        var parts = new List<string> { ReadRefPart() };
        while (Current.Type == TokenType.Separator)
        {
            Advance();
            parts.Add(ReadRefPart());
        }

        Expect(TokenType.RightParen, "Esperado ')' fechando a referência de política.");

        if (parts.Count is < 2 or > 3)
        {
            throw new FormulaException(
                "Referência de política: use (Política;Pontos|Limite|Resposta) ou (Política;Variaveis;nome).", open.Position);
        }

        var category = parts[1];
        var normalized = category.Trim().ToLowerInvariant();
        var known = normalized is "pontos" or "limite" or "resposta" or "variaveis" or "variáveis";
        if (!known)
        {
            throw new FormulaException(
                $"Categoria de política inválida: '{category}'. Use Pontos, Limite, Resposta ou Variaveis.", open.Position);
        }

        if ((normalized is "variaveis" or "variáveis") && parts.Count != 3)
        {
            throw new FormulaException(
                "Para Variaveis, informe o nome: (Política;Variaveis;nome).", open.Position);
        }

        var variable = parts.Count == 3 ? parts[2] : string.Empty;
        return new PolicyRefNode(parts[0], category, variable);
    }

    /// <summary>Reads one part of an external reference (an identifier or text).</summary>
    private string ReadRefPart()
    {
        var token = Current;
        if (token.Type == TokenType.Identifier)
        {
            Advance();
            return token.Lexeme;
        }
        if (token.Type == TokenType.String)
        {
            Advance();
            return token.Text ?? string.Empty;
        }
        throw new FormulaException(
            "Parte inválida na referência externa; use nomes como [SERASA;Score;Pontuacao].", token.Position);
    }

    private FormulaNode ParseFunctionCall(string name)
    {
        Expect(TokenType.LeftParen, "Esperado '(' após o nome da função.");

        var args = new List<FormulaNode>();
        if (Current.Type != TokenType.RightParen)
        {
            args.Add(ParseExpression());
            while (Current.Type == TokenType.Separator)
            {
                Advance();
                args.Add(ParseExpression());
            }
        }

        Expect(TokenType.RightParen, $"Esperado ')' ou ';' nos argumentos de {name}.");
        return new FunctionNode(name, args);
    }

    // --- Helpers ----------------------------------------------------------

    private Token Current => _tokens[_index];

    /// <summary>Espia o token em <paramref name="offset"/> posições à frente.</summary>
    private Token PeekAt(int offset)
    {
        var i = _index + offset;
        return i < _tokens.Count ? _tokens[i] : _tokens[^1];
    }

    private void Advance()
    {
        if (_index < _tokens.Count - 1) _index++;
    }

    private void Expect(TokenType type, string message)
    {
        if (Current.Type != type)
        {
            throw new FormulaException(message, Current.Position);
        }
        Advance();
    }
}

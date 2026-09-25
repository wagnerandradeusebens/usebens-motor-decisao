using MotorDecisao.Application.Execution;
using MotorDecisao.Application.Formulas;
using MotorDecisao.Application.Formulas.Parsing;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Application.Flows;

/// <summary>Gravidade de um aviso de validação do grafo.</summary>
public enum ValidationSeverity
{
    /// <summary>Impede a execução correta (fórmula não compila, PROCV sem chave).</summary>
    Error,

    /// <summary>Não impede, mas provavelmente é um engano (ex.: coluna de retorno ausente).</summary>
    Warning,
}

/// <summary>
/// Um aviso de validação: onde (rótulo amigável do local) e a mensagem pt-BR.
/// Serializado no corpo do save para o editor exibir ao usuário.
/// </summary>
public sealed record ValidationWarning(ValidationSeverity Severity, string Where, string Message);

/// <summary>Config mínima de uma tabela para validar PROCV (local ou global).</summary>
public sealed record TableShape(
    string Name,
    IReadOnlyList<string> ColumnNames,
    string? KeyColumn,
    string? MinColumn,
    string? MaxColumn);

/// <summary>
/// Valida o grafo de uma versão SEM bloquear o salvamento: compila cada fórmula
/// (coletando TODOS os erros, diferente do publish que para no primeiro) e
/// verifica as chamadas de PROCV/PROCV.FAIXA contra a configuração das tabelas
/// (existência, coluna-chave/mín-máx marcada, coluna de retorno). Devolve a lista
/// de avisos que o editor mostra ao usuário ao salvar.
/// </summary>
public static class VersionGraphValidator
{
    /// <param name="graph">O grafo sendo salvo (contém as tabelas locais).</param>
    /// <param name="globalTables">Tabelas globais disponíveis (por nome).</param>
    public static IReadOnlyList<ValidationWarning> Validate(
        VersionGraph graph, IReadOnlyList<TableShape> globalTables)
    {
        var warnings = new List<ValidationWarning>();

        // Índice de tabelas por nome (case-insensitive): locais têm prioridade,
        // depois as globais — mesma precedência da resolução em runtime.
        var tables = new Dictionary<string, TableShape>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in globalTables)
        {
            tables[g.Name] = g;
        }
        foreach (var t in graph.Tables ?? Array.Empty<GraphTable>())
        {
            tables[t.Name] = new TableShape(
                t.Name, t.Columns.Select(c => c.Name).ToList(), t.KeyColumn, t.MinColumn, t.MaxColumn);
        }

        // Compila (best-effort) e valida cada expressão do grafo, com um rótulo
        // amigável do local — reaproveita a mesma nomenclatura do compilador.
        void Check(string? expression, string where)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return;
            }
            if (!FormulaEngine.TryCompile(expression, out _, out var error))
            {
                warnings.Add(new ValidationWarning(
                    ValidationSeverity.Error, where,
                    $"Fórmula inválida: {error!.Message} (posição {error.Position})."));
                return;
            }
            // Compilou: valida as chamadas de PROCV contra as tabelas.
            var ast = Parser.Parse(expression);
            foreach (var w in TableCallCollector.Collect(ast, tables, where))
            {
                warnings.Add(w);
            }
        }

        void CheckActions(IReadOnlyList<ActionItem>? actions, string where)
        {
            if (actions is null) return;
            foreach (var a in actions)
            {
                Check(a.Expression, where);
            }
        }

        foreach (var node in graph.Nodes)
        {
            switch (node.Kind)
            {
                case FlowNodeKind.Condition:
                    if (NodeConfig.TryDeserialize<ConditionConfig>(node.Config, out var cond) && cond is not null)
                    {
                        Check(cond.Expression, $"condição '{node.Label}'");
                        CheckActions(cond.TrueActions, $"ações (verdadeiro) em '{node.Label}'");
                        CheckActions(cond.FalseActions, $"ações (falso) em '{node.Label}'");
                    }
                    break;
                case FlowNodeKind.Computation:
                    if (NodeConfig.TryDeserialize<ComputationConfig>(node.Config, out var comp) && comp is not null)
                    {
                        foreach (var a in comp.Assignments)
                        {
                            Check(a.Expression, $"cálculo '{a.TargetField}' em '{node.Label}'");
                        }
                        CheckActions(comp.Actions, $"ações em '{node.Label}'");
                    }
                    break;
                case FlowNodeKind.Decision:
                    if (NodeConfig.TryDeserialize<DecisionConfig>(node.Config, out var dec) && dec is not null)
                    {
                        CheckActions(dec.Actions, $"ações em '{node.Label}'");
                    }
                    break;
                case FlowNodeKind.DataSource:
                    if (NodeConfig.TryDeserialize<DataSourceConfig>(node.Config, out var ds) && ds is not null)
                    {
                        CheckActions(ds.Actions, $"ações em '{node.Label}'");
                    }
                    break;
                case FlowNodeKind.Action:
                    if (NodeConfig.TryDeserialize<ActionsConfig>(node.Config, out var act) && act is not null)
                    {
                        CheckActions(act.Actions, $"ação em '{node.Label}'");
                    }
                    break;
                case FlowNodeKind.Matrix:
                    if (NodeConfig.TryDeserialize<MatrixConfig>(node.Config, out var mx) && mx is not null)
                    {
                        Check(mx.RowExpression, $"linha da matriz '{node.Label}'");
                        Check(mx.ColExpression, $"coluna da matriz '{node.Label}'");
                    }
                    break;
            }
        }

        foreach (var rs in graph.Rulesets)
        {
            foreach (var r in rs.Rules)
            {
                Check(r.ConditionExpression, $"regra '{r.Name}' em '{rs.Name}'");
            }
        }

        foreach (var f in graph.Formulas)
        {
            Check(f.Expression, $"variável '{f.Key}'");
        }

        return warnings;
    }

    /// <summary>
    /// Visitor que percorre a AST atrás de chamadas de PROCV/PROCV.FAIXA e valida
    /// cada uma contra a configuração das tabelas: tabela existe, tem coluna-chave
    /// (PROCV) ou mín/máx (PROCV.FAIXA) e a coluna de retorno existe. Só considera
    /// argumentos literais de texto (o caso comum); expressões dinâmicas são
    /// ignoradas (não dá para validar sem executar).
    /// </summary>
    private sealed class TableCallCollector : IFormulaNodeVisitor<bool>
    {
        private readonly IReadOnlyDictionary<string, TableShape> _tables;
        private readonly string _where;
        private readonly List<ValidationWarning> _warnings = new();

        private TableCallCollector(IReadOnlyDictionary<string, TableShape> tables, string where)
        {
            _tables = tables;
            _where = where;
        }

        public static IReadOnlyList<ValidationWarning> Collect(
            FormulaNode node, IReadOnlyDictionary<string, TableShape> tables, string where)
        {
            var collector = new TableCallCollector(tables, where);
            node.Accept(collector);
            return collector._warnings;
        }

        public bool VisitFunction(FunctionNode node)
        {
            if (TableFunctions.IsTableFunction(node.Name))
            {
                ValidateProcv(node);
            }
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
            }
            return true;
        }

        private void ValidateProcv(FunctionNode node)
        {
            var isRange = string.Equals(node.Name, TableFunctions.Range, StringComparison.OrdinalIgnoreCase);
            var tableName = LiteralText(node.Arguments.Count > 0 ? node.Arguments[0] : null);
            var returnColumn = LiteralText(node.Arguments.Count > 1 ? node.Arguments[1] : null);

            // Nome da tabela dinâmico (não literal): não dá para validar estaticamente.
            if (tableName is null)
            {
                return;
            }

            if (!_tables.TryGetValue(tableName, out var table))
            {
                _warnings.Add(new ValidationWarning(
                    ValidationSeverity.Error, _where,
                    $"{node.Name} referencia a tabela \"{tableName}\", que não existe (local ou global)."));
                return;
            }

            if (isRange)
            {
                if (string.IsNullOrWhiteSpace(table.MinColumn) && string.IsNullOrWhiteSpace(table.MaxColumn))
                {
                    _warnings.Add(new ValidationWarning(
                        ValidationSeverity.Error, _where,
                        $"PROCV.FAIXA na tabela \"{tableName}\" exige uma coluna de mínimo e/ou máximo, " +
                        "mas nenhuma está definida (edite a tabela em \"Como consultar\")."));
                }
            }
            else if (string.IsNullOrWhiteSpace(table.KeyColumn))
            {
                _warnings.Add(new ValidationWarning(
                    ValidationSeverity.Error, _where,
                    $"PROCV na tabela \"{tableName}\" exige uma coluna-chave, mas nenhuma está marcada " +
                    "(edite a tabela em \"Como consultar\" → \"Coluna-chave\")."));
            }

            // Coluna de retorno literal que não existe na tabela.
            if (returnColumn is not null &&
                !table.ColumnNames.Any(c => string.Equals(c, returnColumn, StringComparison.OrdinalIgnoreCase)))
            {
                _warnings.Add(new ValidationWarning(
                    ValidationSeverity.Warning, _where,
                    $"{node.Name} retorna a coluna \"{returnColumn}\", que não existe na tabela \"{tableName}\"."));
            }
        }

        /// <summary>Texto de um argumento literal de texto; null se não for literal texto.</summary>
        private static string? LiteralText(FormulaNode? node)
            => node is LiteralNode lit && lit.Value.Type == FormulaValueType.Text ? lit.Value.AsText() : null;

        // Percorre o resto da árvore atrás de outras chamadas.
        public bool VisitUnary(UnaryNode node) => node.Operand.Accept(this);
        public bool VisitBinary(BinaryNode node) { node.Left.Accept(this); node.Right.Accept(this); return true; }
        public bool VisitLiteral(LiteralNode node) => true;
        public bool VisitField(FieldNode node) => true;
        public bool VisitExternalRef(ExternalRefNode node) => true;
        public bool VisitVariableRef(VariableRefNode node) => true;
        public bool VisitPolicyRef(PolicyRefNode node) => true;
    }
}

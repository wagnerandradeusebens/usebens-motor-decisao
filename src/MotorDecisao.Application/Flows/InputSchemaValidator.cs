using MotorDecisao.Application.Formulas;

namespace MotorDecisao.Application.Flows;

/// <summary>
/// Valida os campos de entrada de uma decisão contra o schema da política:
/// nenhum campo obrigatório pode faltar. Regra única compartilhada entre a
/// decisão de produção e a de teste.
/// </summary>
public static class InputSchemaValidator
{
    /// <summary>
    /// Retorna uma mensagem de erro (pt-BR) quando há campos obrigatórios
    /// ausentes/vazios na entrada, ou <c>null</c> quando está tudo presente.
    /// Um campo é considerado presente quando existe na entrada e não é branco
    /// (nem texto vazio).
    /// </summary>
    public static string? Validate(
        PolicyInputSchema schema, IReadOnlyDictionary<string, FormulaValue> input)
    {
        var missing = new List<InputSchemaField>();
        foreach (var field in schema.Fields)
        {
            if (!field.Required) continue;
            if (IsPresent(input, field.Name)) continue;
            missing.Add(field);
        }

        if (missing.Count == 0)
        {
            return null;
        }

        var parts = missing.Select(f =>
        {
            if (f.Origin == InputFieldOrigin.Source && f.RequiredBySources.Count > 0)
            {
                return $"'{f.Name}' (exigido por {string.Join(", ", f.RequiredBySources)})";
            }
            return $"'{f.Name}'";
        });

        return "Campos obrigatórios ausentes na requisição: " + string.Join("; ", parts) + ".";
    }

    private static bool IsPresent(IReadOnlyDictionary<string, FormulaValue> input, string name)
    {
        // Busca case-insensitive (a entrada pode vir com caixa diferente).
        foreach (var kv in input)
        {
            if (!string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Value;
            if (v.IsBlank) return false;
            if (v.Type == FormulaValueType.Text && string.IsNullOrWhiteSpace(v.AsText())) return false;
            return true;
        }
        return false;
    }
}

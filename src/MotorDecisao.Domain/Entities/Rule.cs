using MotorDecisao.Domain.Common;
using MotorDecisao.Domain.Enums;

namespace MotorDecisao.Domain.Entities;

/// <summary>
/// A single "if condition then effect" rule inside a <see cref="Ruleset"/>. The
/// condition is an Excel-like boolean formula evaluated against the proposal data;
/// when it matches, the rule applies its <see cref="Effect"/>.
/// </summary>
public class Rule : Entity
{
    /// <summary>Owning ruleset.</summary>
    public Guid RulesetId { get; set; }
    public Ruleset? Ruleset { get; set; }

    /// <summary>Evaluation order within the ruleset (ascending).</summary>
    public int Order { get; set; }

    /// <summary>Short name of the rule (e.g. "Age below minimum").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Excel-like boolean expression that decides whether the rule fires,
    /// e.g. <c>AND(age &gt;= 18; income &gt; 2000)</c>.
    /// </summary>
    public string ConditionExpression { get; set; } = string.Empty;

    /// <summary>What happens when the condition is true.</summary>
    public RuleEffect Effect { get; set; }

    /// <summary>
    /// Points added to the scorecard when <see cref="Effect"/> is
    /// <see cref="RuleEffect.Score"/>. May be negative.
    /// </summary>
    public decimal ScoreWeight { get; set; }

    /// <summary>
    /// Outcome forced when <see cref="Effect"/> is <see cref="RuleEffect.Decision"/>.
    /// </summary>
    public DecisionOutcome? ForcedOutcome { get; set; }

    /// <summary>Message/flag attached to the execution trace when the rule fires.</summary>
    public string? Message { get; set; }

    /// <summary>Whether the rule participates in evaluation.</summary>
    public bool IsEnabled { get; set; } = true;
}

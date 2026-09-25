import type { DecisionOutcome, GraphRule, GraphRuleset, RuleEffect } from '../api/types';

const EFFECTS: RuleEffect[] = ['Score', 'Decision', 'Annotation'];
const OUTCOMES: DecisionOutcome[] = ['Approved', 'ApprovedWithConditions', 'ManualReview', 'Denied'];

interface Props {
  rulesets: GraphRuleset[];
  readOnly: boolean;
  onChange: (rulesets: GraphRuleset[]) => void;
}

/** Editor for the flow's rulesets/scorecards and their rules. */
export function RulesetEditor({ rulesets, readOnly, onChange }: Props) {
  function addRuleset() {
    const key = `rs-${Date.now()}`;
    onChange([
      ...rulesets,
      { rulesetKey: key, name: 'Novo conjunto', description: null, approvalThreshold: null, rules: [] },
    ]);
  }

  function updateRuleset(idx: number, patch: Partial<GraphRuleset>) {
    const next = [...rulesets];
    next[idx] = { ...next[idx], ...patch };
    onChange(next);
  }

  function addRule(idx: number) {
    const rs = rulesets[idx];
    const rule: GraphRule = {
      order: rs.rules.length + 1,
      name: 'Nova regra',
      conditionExpression: '',
      effect: 'Score',
      scoreWeight: 0,
      forcedOutcome: null,
      message: null,
      isEnabled: true,
    };
    updateRuleset(idx, { rules: [...rs.rules, rule] });
  }

  function updateRule(rsIdx: number, ruleIdx: number, patch: Partial<GraphRule>) {
    const rs = rulesets[rsIdx];
    const rules = [...rs.rules];
    rules[ruleIdx] = { ...rules[ruleIdx], ...patch };
    updateRuleset(rsIdx, { rules });
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h3 style={{ margin: 0 }}>Conjuntos de regras</h3>
        {!readOnly && <button className="btn btn--secondary btn--sm" onClick={addRuleset}>+ Conjunto</button>}
      </div>

      {rulesets.length === 0 && <div className="muted">Nenhum conjunto de regras.</div>}

      {rulesets.map((rs, i) => (
        <div className="card" key={rs.rulesetKey}>
          <input
            disabled={readOnly}
            value={rs.name}
            onChange={(e) => updateRuleset(i, { name: e.target.value })}
            style={{ fontWeight: 600, marginBottom: 8 }}
          />
          {rs.rules.map((r, j) => (
            <div key={j} className="stack" style={{ border: '1px solid var(--border)', borderRadius: 6, padding: 8, marginBottom: 8 }}>
              <input disabled={readOnly} value={r.name} onChange={(e) => updateRule(i, j, { name: e.target.value })} placeholder="nome" />
              <textarea
                disabled={readOnly}
                rows={2}
                value={r.conditionExpression}
                onChange={(e) => updateRule(i, j, { conditionExpression: e.target.value })}
                placeholder="condição (ex.: score > 600)"
              />
              <div className="row" style={{ gap: 8 }}>
                <select disabled={readOnly} value={r.effect} onChange={(e) => updateRule(i, j, { effect: e.target.value as RuleEffect })}>
                  {EFFECTS.map((ef) => (
                    <option key={ef} value={ef}>
                      {ef}
                    </option>
                  ))}
                </select>
                {r.effect === 'Score' && (
                  <input
                    disabled={readOnly}
                    type="number"
                    value={r.scoreWeight}
                    onChange={(e) => updateRule(i, j, { scoreWeight: Number(e.target.value) })}
                    placeholder="pontos"
                  />
                )}
                {r.effect === 'Decision' && (
                  <select
                    disabled={readOnly}
                    value={r.forcedOutcome ?? ''}
                    onChange={(e) => updateRule(i, j, { forcedOutcome: (e.target.value || null) as DecisionOutcome | null })}
                  >
                    <option value="">— desfecho —</option>
                    {OUTCOMES.map((o) => (
                      <option key={o} value={o}>
                        {o}
                      </option>
                    ))}
                  </select>
                )}
              </div>
              <input
                disabled={readOnly}
                value={r.message ?? ''}
                onChange={(e) => updateRule(i, j, { message: e.target.value || null })}
                placeholder="mensagem (trilha)"
              />
            </div>
          ))}
          {!readOnly && <button className="btn btn--secondary btn--sm" onClick={() => addRule(i)}>+ Regra</button>}
        </div>
      ))}
    </div>
  );
}

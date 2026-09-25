import type {
  ActionsConfig,
  ActionType,
  CommentConfig,
  ComputationConfig,
  ConditionConfig,
  DataSourceConfig,
  DecisionConfig,
  DecisionOutcome,
  FlowNodeKind,
  GraphRuleset,
  MatrixBand,
  MatrixConfig,
  MatrixMode,
  SourceDescriptorDto,
} from '../api/types';
import { FormulaInput } from './FormulaInput';

const ACTION_LABELS: { value: ActionType; label: string; needsName?: boolean }[] = [
  { value: 'AddPoints', label: 'Adiciona aos pontos' },
  { value: 'SetPoints', label: 'Define pontos' },
  { value: 'AddLimit', label: 'Adiciona ao limite' },
  { value: 'SetLimit', label: 'Define limite' },
  { value: 'AddJustification', label: 'Adiciona à justificativa' },
  { value: 'SetJustification', label: 'Define justificativa' },
  { value: 'SetOutput', label: 'Define parâmetro de saída', needsName: true },
];

const OUTCOMES: DecisionOutcome[] = [
  'Approved',
  'ApprovedWithConditions',
  'ManualReview',
  'Denied',
];

const FORMULA_HINT =
  "Campos 'campo', variáveis {variavel}, texto \"texto\", fontes [Fonte;Produto;Dado], funções (SE, ARRED…). Separador: ;";

interface Props {
  nodeKey: string;
  kind: FlowNodeKind;
  label: string;
  config: string;
  rulesetKey: string | null;
  rulesets: GraphRuleset[];
  readOnly: boolean;
  fields: string[];
  variables: string[];
  sources: SourceDescriptorDto[];
  onLabelChange: (label: string) => void;
  onConfigChange: (config: string) => void;
  onRulesetKeyChange: (rulesetKey: string | null) => void;
}

function parse<T>(json: string, fallback: T): T {
  try {
    return { ...fallback, ...(JSON.parse(json) as object) } as T;
  } catch {
    return fallback;
  }
}

/** Right-hand panel to edit the selected node's label and typed config. */
export function NodeInspector(props: Props) {
  const { kind, config, readOnly } = props;

  // Rendered as a function (not useMemo): memoizing this JSX on `props` — a fresh
  // object each render — gave no benefit and caused inputs to lose focus/updates.
  const renderBody = () => {
    switch (kind) {
      case 'Condition': {
        const cfg = parse<ConditionConfig>(config, { expression: '' });
        return (
          <div>
            <label className="form-label">Expressão (booleana)</label>
            <FormulaInput
              rows={3}
              disabled={readOnly}
              value={cfg.expression}
              onChange={(v) => props.onConfigChange(JSON.stringify({ expression: v }))}
              fields={props.fields}
              variables={props.variables}
              sources={props.sources}
              placeholder="Ex.: 'idade' >= 18"
            />
            <div className="hint">{FORMULA_HINT}</div>
            <div className="hint">Saídas: verde = verdadeiro, vermelho = falso.</div>
          </div>
        );
      }
      case 'Computation': {
        const cfg = parse<ComputationConfig>(config, { assignments: [] });
        const update = (assignments: ComputationConfig['assignments']) =>
          props.onConfigChange(JSON.stringify({ assignments }));
        return (
          <div className="stack">
            <label className="form-label">Cálculos</label>
            {cfg.assignments.map((a, i) => (
              <div key={i} className="stack" style={{ border: '1px solid var(--border)', borderRadius: 6, padding: 8 }}>
                <input
                  disabled={readOnly}
                  placeholder="campo destino (ex.: comprometimento)"
                  value={a.targetField}
                  onChange={(e) => {
                    const next = [...cfg.assignments];
                    next[i] = { ...a, targetField: e.target.value };
                    update(next);
                  }}
                />
                <FormulaInput
                  rows={2}
                  disabled={readOnly}
                  placeholder="expressão (ex.: 'divida' / 'renda')"
                  value={a.expression}
                  fields={props.fields}
                  variables={props.variables}
                  sources={props.sources}
                  onChange={(v) => {
                    const next = [...cfg.assignments];
                    next[i] = { ...a, expression: v };
                    update(next);
                  }}
                />
                {!readOnly && (
                  <button className="btn btn--secondary btn--sm" onClick={() => update(cfg.assignments.filter((_, j) => j !== i))}>Remover</button>
                )}
              </div>
            ))}
            {!readOnly && (
              <button className="btn btn--secondary btn--sm" onClick={() => update([...cfg.assignments, { targetField: '', expression: '' }])}>
                + Adicionar cálculo
              </button>
            )}
            <div className="hint">{FORMULA_HINT}</div>
          </div>
        );
      }
      case 'Decision': {
        const cfg = parse<DecisionConfig>(config, { outcome: 'Approved', message: '' });
        return (
          <div className="stack">
            <div>
              <label className="form-label">Desfecho</label>
              <select
                disabled={readOnly}
                value={cfg.outcome}
                onChange={(e) => props.onConfigChange(JSON.stringify({ ...cfg, outcome: e.target.value }))}
              >
                {OUTCOMES.map((o) => (
                  <option key={o} value={o}>
                    {o}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="form-label">Mensagem</label>
              <input
                disabled={readOnly}
                value={cfg.message ?? ''}
                onChange={(e) => props.onConfigChange(JSON.stringify({ ...cfg, message: e.target.value }))}
                placeholder="Opcional"
              />
            </div>
          </div>
        );
      }
      case 'DataSource': {
        const cfg = parse<DataSourceConfig>(config, { source: '' });
        return (
          <div>
            <label className="form-label">Fonte de dados</label>
            <input
              disabled={readOnly}
              value={cfg.source}
              onChange={(e) => props.onConfigChange(JSON.stringify({ source: e.target.value }))}
              placeholder="Ex.: bureau_serasa"
            />
            <div className="hint">Integração externa é resolvida no backend (extensão futura).</div>
          </div>
        );
      }
      case 'Action': {
        const cfg = parse<ActionsConfig>(config, { actions: [] });
        const update = (actions: ActionsConfig['actions']) => props.onConfigChange(JSON.stringify({ actions }));
        return (
          <div className="stack">
            <label className="form-label">Ações (executadas em ordem)</label>
            {cfg.actions.map((a, i) => {
              const meta = ACTION_LABELS.find((x) => x.value === a.type);
              return (
                <div key={i} className="stack" style={{ border: '1px solid var(--cor-borda)', borderRadius: 6, padding: 8 }}>
                  <select
                    disabled={readOnly}
                    value={a.type}
                    onChange={(e) => {
                      const next = [...cfg.actions];
                      next[i] = { ...a, type: e.target.value as ActionType };
                      update(next);
                    }}
                  >
                    {ACTION_LABELS.map((x) => <option key={x.value} value={x.value}>{x.label}</option>)}
                  </select>
                  {meta?.needsName && (
                    <input
                      disabled={readOnly}
                      placeholder="nome do parâmetro (ex.: taxa)"
                      value={a.name ?? ''}
                      onChange={(e) => {
                        const next = [...cfg.actions];
                        next[i] = { ...a, name: e.target.value };
                        update(next);
                      }}
                    />
                  )}
                  <FormulaInput
                    rows={2}
                    disabled={readOnly}
                    placeholder='expressão (ex.: 30  ou  "cliente ok")'
                    value={a.expression}
                    fields={props.fields}
                    variables={props.variables}
                    sources={props.sources}
                    onChange={(v) => {
                      const next = [...cfg.actions];
                      next[i] = { ...a, expression: v };
                      update(next);
                    }}
                  />
                  {!readOnly && <button className="btn btn--secondary btn--sm" onClick={() => update(cfg.actions.filter((_, j) => j !== i))}>Remover</button>}
                </div>
              );
            })}
            {!readOnly && (
              <button className="btn btn--secondary btn--sm" onClick={() => update([...cfg.actions, { type: 'AddPoints', expression: '' }])}>
                + Adicionar ação
              </button>
            )}
            <div className="hint">Contadores: pontos e limite. Leia-os nas fórmulas como <code>'pontos'</code> e <code>'limite'</code>.</div>
          </div>
        );
      }
      case 'Matrix': {
        const cfg = parse<MatrixConfig>(config, {
          mode: 'Points', rowExpression: '', colExpression: '',
          rowBands: [], colBands: [], cells: [], defaultValue: '0',
        });
        const commit = (next: MatrixConfig) => props.onConfigChange(JSON.stringify(next));

        // Keeps the cell grid rectangular (rows x cols) when bands change.
        const resizeCells = (rows: number, cols: number, cells: string[][]) =>
          Array.from({ length: rows }, (_, r) =>
            Array.from({ length: cols }, (_, c) => cells[r]?.[c] ?? (cfg.mode === 'Decision' ? 'ManualReview' : '0')));

        const setBands = (which: 'rowBands' | 'colBands', bands: MatrixBand[]) => {
          const rows = which === 'rowBands' ? bands.length : cfg.rowBands.length;
          const cols = which === 'colBands' ? bands.length : cfg.colBands.length;
          commit({ ...cfg, [which]: bands, cells: resizeCells(rows, cols, cfg.cells) } as MatrixConfig);
        };

        const bandEditor = (which: 'rowBands' | 'colBands', title: string) => {
          const bands = cfg[which];
          return (
            <div>
              <div className="form-label">{title}</div>
              {bands.map((b, i) => (
                <div key={i} className="row" style={{ gap: 4, marginBottom: 4 }}>
                  <input disabled={readOnly} placeholder="rótulo" value={b.label}
                    onChange={(e) => { const n = [...bands]; n[i] = { ...b, label: e.target.value }; setBands(which, n); }} />
                  <input disabled={readOnly} type="number" placeholder="min" value={b.min ?? ''} style={{ width: 70 }}
                    onChange={(e) => { const n = [...bands]; n[i] = { ...b, min: e.target.value === '' ? null : Number(e.target.value) }; setBands(which, n); }} />
                  <input disabled={readOnly} type="number" placeholder="max" value={b.max ?? ''} style={{ width: 70 }}
                    onChange={(e) => { const n = [...bands]; n[i] = { ...b, max: e.target.value === '' ? null : Number(e.target.value) }; setBands(which, n); }} />
                  {!readOnly && <button className="btn btn--secondary btn--sm" onClick={() => setBands(which, bands.filter((_, j) => j !== i))}>×</button>}
                </div>
              ))}
              {!readOnly && <button className="btn btn--secondary btn--sm" onClick={() => setBands(which, [...bands, { label: '', min: null, max: null }])}>+ Faixa</button>}
            </div>
          );
        };

        return (
          <div className="stack">
            <div>
              <label className="form-label">Modo</label>
              <select disabled={readOnly} value={cfg.mode} onChange={(e) => commit({ ...cfg, mode: e.target.value as MatrixMode })}>
                <option value="Points">Pontos</option>
                <option value="Limit">Limite</option>
                <option value="Decision">Decisão</option>
              </select>
            </div>
            <div>
              <label className="form-label">Expressão das linhas</label>
              <FormulaInput rows={1} disabled={readOnly} value={cfg.rowExpression}
                fields={props.fields} variables={props.variables} sources={props.sources}
                onChange={(v) => commit({ ...cfg, rowExpression: v })} placeholder="ex.: 'renda'" />
            </div>
            <div>
              <label className="form-label">Expressão das colunas</label>
              <FormulaInput rows={1} disabled={readOnly} value={cfg.colExpression}
                fields={props.fields} variables={props.variables} sources={props.sources}
                onChange={(v) => commit({ ...cfg, colExpression: v })} placeholder="ex.: [SERASA;Score;Pontuacao]" />
            </div>
            {bandEditor('rowBands', 'Faixas das linhas')}
            {bandEditor('colBands', 'Faixas das colunas')}

            {cfg.rowBands.length > 0 && cfg.colBands.length > 0 && (
              <div style={{ overflowX: 'auto' }}>
                <div className="form-label">Células ({cfg.mode === 'Decision' ? 'desfecho' : 'valor'})</div>
                <table style={{ borderCollapse: 'collapse', fontSize: '0.78rem' }}>
                  <thead>
                    <tr>
                      <th></th>
                      {cfg.colBands.map((c, ci) => <th key={ci} style={{ padding: 3 }}>{c.label || `col ${ci + 1}`}</th>)}
                    </tr>
                  </thead>
                  <tbody>
                    {cfg.rowBands.map((r, ri) => (
                      <tr key={ri}>
                        <th style={{ padding: 3, textAlign: 'right' }}>{r.label || `lin ${ri + 1}`}</th>
                        {cfg.colBands.map((_, ci) => (
                          <td key={ci} style={{ padding: 2 }}>
                            {cfg.mode === 'Decision' ? (
                              <select disabled={readOnly} value={cfg.cells[ri]?.[ci] ?? 'ManualReview'} style={{ width: 130 }}
                                onChange={(e) => { const cells = resizeCells(cfg.rowBands.length, cfg.colBands.length, cfg.cells); cells[ri][ci] = e.target.value; commit({ ...cfg, cells }); }}>
                                {OUTCOMES.map((o) => <option key={o} value={o}>{o}</option>)}
                              </select>
                            ) : (
                              <input disabled={readOnly} type="number" value={cfg.cells[ri]?.[ci] ?? '0'} style={{ width: 70 }}
                                onChange={(e) => { const cells = resizeCells(cfg.rowBands.length, cfg.colBands.length, cfg.cells); cells[ri][ci] = e.target.value; commit({ ...cfg, cells }); }} />
                            )}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <div className="hint">Faixas: min ≤ valor &lt; max (vazio = aberto). Célula padrão quando nada casa.</div>
          </div>
        );
      }
      case 'Ruleset': {
        return (
          <div>
            <label className="form-label">Conjunto de regras</label>
            <select
              disabled={readOnly}
              value={props.rulesetKey ?? ''}
              onChange={(e) => props.onRulesetKeyChange(e.target.value || null)}
            >
              <option value="">— selecione —</option>
              {props.rulesets.map((rs) => (
                <option key={rs.rulesetKey} value={rs.rulesetKey}>
                  {rs.name}
                </option>
              ))}
            </select>
            <div className="hint">Gerencie os conjuntos de regras na aba de regras.</div>
          </div>
        );
      }
      case 'Comment': {
        const cfg = parse<CommentConfig>(config, { text: '' });
        return (
          <div>
            <label className="form-label">Texto do comentário</label>
            <textarea
              disabled={readOnly}
              rows={4}
              value={cfg.text}
              onChange={(e) => {
                const text = e.target.value;
                props.onConfigChange(JSON.stringify({ text }));
                // The canvas note shows the label, so keep it in sync with the text.
                props.onLabelChange(text);
              }}
              placeholder="Anotação visível no fluxo. Não afeta a execução."
            />
            <div className="hint">Puramente visual: ignorado pela execução.</div>
          </div>
        );
      }
      default:
        return <div className="muted">Nó inicial — sem configuração.</div>;
    }
  };

  const isComment = kind === 'Comment';

  return (
    <div className="stack">
      {!isComment && (
        <div>
          <label className="form-label">Rótulo</label>
          <input disabled={readOnly} value={props.label} onChange={(e) => props.onLabelChange(e.target.value)} />
        </div>
      )}
      {renderBody()}
    </div>
  );
}

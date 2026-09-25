import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ExecutionDetail, ExecutionSummary, TraceCategory, TraceStep } from '../api/types';

function fmt(date: string | null | undefined): string {
  return date ? new Date(date).toLocaleString('pt-BR') : '—';
}

/** Human-readable label for each trace block, in the order we display them. */
const CATEGORY_ORDER: TraceCategory[] = [
  'Inicio', 'Fonte', 'Variavel', 'Regra', 'Acao', 'Matriz', 'Fluxo', 'Decisao',
];
const CATEGORY_LABEL: Record<TraceCategory, string> = {
  Inicio: 'Início',
  Fonte: 'Fontes',
  Variavel: 'Variáveis',
  Regra: 'Regras',
  Acao: 'Ações',
  Matriz: 'Matrizes',
  Fluxo: 'Fluxo',
  Decisao: 'Decisão',
};

/** Groups trace steps by category, preserving each step's original order. */
function groupByCategory(trace: TraceStep[]): { category: TraceCategory; steps: TraceStep[] }[] {
  const buckets = new Map<TraceCategory, TraceStep[]>();
  for (const step of trace) {
    const cat = step.category ?? 'Fluxo';
    if (!buckets.has(cat)) buckets.set(cat, []);
    buckets.get(cat)!.push(step);
  }
  const ordered = CATEGORY_ORDER.filter((c) => buckets.has(c)).map((c) => ({ category: c, steps: buckets.get(c)! }));
  // Any unexpected category still gets shown at the end.
  for (const [cat, steps] of buckets) {
    if (!CATEGORY_ORDER.includes(cat)) ordered.push({ category: cat, steps });
  }
  return ordered;
}

/** Pretty-prints a JSON string, falling back to the raw value. */
function pretty(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

/** Triggers a browser download of the given content. */
function download(filename: string, content: string, mime: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

/** Builds a human-readable text log of an execution. */
function toTextLog(d: ExecutionDetail): string {
  const lines: string[] = [];
  lines.push(`Execução: ${d.id}`);
  lines.push(`Referência: ${d.proposalReference ?? '—'}`);
  lines.push(`Desfecho: ${d.outcome}  |  Pontos: ${d.score}  |  Limite: ${d.limit}  |  Status: ${d.status}`);
  lines.push(`Criada: ${fmt(d.createdAt)}  |  Concluída: ${fmt(d.completedAt)}`);
  if (d.error) lines.push(`Erro: ${d.error}`);
  lines.push('');
  lines.push('Entrada:');
  lines.push(pretty(d.inputData));
  if (d.justifications.length) {
    lines.push('');
    lines.push('Justificativas:');
    d.justifications.forEach((j) => lines.push(`  - ${j}`));
  }
  if (Object.keys(d.outputs).length) {
    lines.push('');
    lines.push('Parâmetros de saída:');
    Object.entries(d.outputs).forEach(([k, v]) => lines.push(`  ${k} = ${v}`));
  }
  lines.push('');
  lines.push('Log passo a passo (por bloco):');
  groupByCategory(d.trace).forEach(({ category, steps }) => {
    lines.push('');
    lines.push(`== ${CATEGORY_LABEL[category] ?? category} ==`);
    steps.forEach((t) => {
      lines.push(`  #${t.sequence} [${t.nodeLabel}]${t.expression ? ` ${t.expression}` : ''}`);
      if (t.result) lines.push(`      resultado: ${t.result}`);
      if (t.message) lines.push(`      ${t.message}`);
      if (t.detail && t.detail.length) {
        lines.push('      resolução:');
        t.detail.forEach((s) => {
          lines.push(`        ${'  '.repeat(s.depth)}${s.expression} = ${s.value}`);
        });
      }
    });
  });
  return lines.join('\n');
}

/**
 * Detailed execution log viewer for a policy: a list of recent executions and,
 * for the selected one, the full step-by-step trace plus input and results.
 */
export function ExecutionsPage() {
  const { flowId } = useParams();
  const [items, setItems] = useState<ExecutionSummary[]>([]);
  const [detail, setDetail] = useState<ExecutionDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!flowId) return;
    (async () => {
      try {
        const list = await api.listExecutions(flowId);
        setItems(list);
        if (list[0]) void open(list[0].id);
      } catch (e) {
        setError(e instanceof ApiError ? e.message : 'Falha ao carregar execuções.');
      } finally {
        setLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [flowId]);

  async function open(id: string) {
    try {
      setDetail(await api.getExecution(id));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao carregar a execução.');
    }
  }

  return (
    <div className="container">
      <div className="row spread">
        <h2 style={{ margin: 0 }}>Execuções</h2>
        <Link to={`/flows/${flowId}`}>← voltar à política</Link>
      </div>

      {error && <div className="error-box">{error}</div>}

      <div style={{ display: 'grid', gridTemplateColumns: '320px 1fr', gap: '1rem', marginTop: '1rem' }}>
        {/* List */}
        <div className="card" style={{ padding: 0, maxHeight: '70vh', overflowY: 'auto' }}>
          {loading ? (
            <p className="muted" style={{ padding: '1rem' }}>Carregando…</p>
          ) : items.length === 0 ? (
            <p className="muted" style={{ padding: '1rem' }}>Nenhuma execução ainda. Rode um teste na política.</p>
          ) : (
            items.map((e) => (
              <div
                key={e.id}
                onClick={() => void open(e.id)}
                style={{
                  padding: '0.6rem 0.8rem', borderBottom: '1px solid var(--cor-borda)', cursor: 'pointer',
                  background: detail?.id === e.id ? 'var(--cor-fundo)' : 'transparent',
                }}
              >
                <div className="row spread">
                  <strong className={`outcome ${e.outcome}`}>{e.outcome}</strong>
                  <span className="muted">{fmt(e.createdAt)}</span>
                </div>
                <div className="muted" style={{ fontSize: '0.78rem' }}>
                  {e.proposalReference ?? 'sem referência'} · pontos {e.score}
                </div>
              </div>
            ))
          )}
        </div>

        {/* Detail */}
        <div className="card" style={{ maxHeight: '70vh', overflowY: 'auto' }}>
          {!detail ? (
            <div className="muted">Selecione uma execução para ver o log detalhado.</div>
          ) : (
            <div className="stack">
              <div className="row spread">
                <strong className={`outcome ${detail.outcome}`}>{detail.outcome}</strong>
                <span className="muted">pontos {detail.score} · limite {detail.limit} · {detail.status}</span>
              </div>
              <div className="row" style={{ gap: 8 }}>
                <button
                  className="btn btn--secondary btn--sm"
                  onClick={() => download(`execucao-${detail.id}.txt`, toTextLog(detail), 'text/plain;charset=utf-8')}
                >
                  Baixar trilha (texto)
                </button>
                <button
                  className="btn btn--secondary btn--sm"
                  onClick={() => download(`execucao-${detail.id}.json`, JSON.stringify(detail, null, 2), 'application/json')}
                >
                  Baixar (JSON)
                </button>
              </div>
              {detail.error && <div className="error-box">{detail.error}</div>}

              <div>
                <div className="form-label">Entrada</div>
                <pre style={{ background: 'var(--cor-fundo)', border: '1px solid var(--cor-borda)', borderRadius: 6, padding: 8, fontSize: '0.78rem', overflowX: 'auto', margin: 0 }}>
                  {pretty(detail.inputData)}
                </pre>
              </div>

              {detail.justifications.length > 0 && (
                <div>
                  <div className="form-label">Justificativas</div>
                  <ul style={{ margin: 0, paddingLeft: '1.1rem' }}>
                    {detail.justifications.map((j, i) => <li key={i} className="muted">{j}</li>)}
                  </ul>
                </div>
              )}

              {Object.keys(detail.outputs).length > 0 && (
                <div>
                  <div className="form-label">Parâmetros de saída</div>
                  {Object.entries(detail.outputs).map(([k, v]) => (
                    <div key={k} className="row spread" style={{ fontSize: '0.82rem' }}><code>{k}</code><span>{v}</span></div>
                  ))}
                </div>
              )}

              <div>
                <div className="form-label">Log passo a passo (por bloco)</div>
                <div className="stack" style={{ gap: 12 }}>
                  {groupByCategory(detail.trace).map(({ category, steps }) => (
                    <div key={category}>
                      <div className="trace-block-title">
                        {CATEGORY_LABEL[category] ?? category}
                        <span className="muted" style={{ fontWeight: 400 }}> · {steps.length}</span>
                      </div>
                      <div className="stack" style={{ gap: 2 }}>
                        {steps.map((t) => <TraceStepRow key={t.sequence} step={t} />)}
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="muted" style={{ fontSize: '0.72rem' }}>
                Execução {detail.id} · criada {fmt(detail.createdAt)} · concluída {fmt(detail.completedAt)}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * A single trace step. When the step carries a deep resolution trace (nested
 * formula), it can be expanded to reveal the step-by-step tree, indented by depth.
 */
function TraceStepRow({ step }: { step: TraceStep }) {
  const [open, setOpen] = useState(false);
  const hasDetail = step.detail && step.detail.length > 0;

  return (
    <div className="trace-step">
      <div
        className="seq"
        onClick={hasDetail ? () => setOpen((v) => !v) : undefined}
        style={{ cursor: hasDetail ? 'pointer' : 'default', userSelect: 'none' }}
      >
        {hasDetail && <span style={{ marginRight: 4 }}>{open ? '▼' : '▶'}</span>}
        #{step.sequence} · {step.nodeLabel}{step.expression ? ` · ${step.expression}` : ''}
      </div>
      {step.result && <div style={{ fontFamily: 'ui-monospace, monospace', fontSize: '0.8rem' }}>{step.result}</div>}
      {step.message && <div className="muted" style={{ fontSize: '0.78rem' }}>{step.message}</div>}
      {hasDetail && open && (
        <div className="trace-detail">
          {step.detail.map((s, i) => (
            <div key={i} style={{ paddingLeft: `${s.depth * 1.1}rem` }}>
              <code>{s.expression}</code> <span className="muted">=</span> <strong>{s.value}</strong>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

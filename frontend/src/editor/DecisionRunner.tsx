import { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { DecisionResponse } from '../api/types';

interface Field {
  key: string;
  value: string;
  type: 'number' | 'text' | 'boolean' | 'date';
}

interface Props {
  flowId: string;
}

/** Coerces a form field into the JSON shape the decision endpoint expects. */
function toValue(f: Field): unknown {
  switch (f.type) {
    case 'number':
      return Number(f.value);
    case 'boolean':
      return f.value === 'true';
    default:
      return f.value; // text + date (yyyy-MM-dd) both go as string
  }
}

/** Runs a test proposal against the published version and shows outcome + trace. */
export function DecisionRunner({ flowId }: Props) {
  const [fields, setFields] = useState<Field[]>([{ key: 'idade', value: '25', type: 'number' }]);
  const [reference, setReference] = useState('TESTE-1');
  const [result, setResult] = useState<DecisionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  function setField(i: number, patch: Partial<Field>) {
    const next = [...fields];
    next[i] = { ...next[i], ...patch };
    setFields(next);
  }

  async function run() {
    setError(null);
    setResult(null);
    setRunning(true);
    try {
      const payload: Record<string, unknown> = {};
      for (const f of fields) {
        if (f.key.trim()) payload[f.key.trim()] = toValue(f);
      }
      setResult(await api.decide(flowId, reference || null, payload));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao executar a decisão.');
    } finally {
      setRunning(false);
    }
  }

  return (
    <div className="stack">
      <h3 style={{ margin: 0 }}>Testar decisão</h3>
      <div>
        <label className="form-label">Referência da proposta</label>
        <input value={reference} onChange={(e) => setReference(e.target.value)} />
      </div>

      <label className="form-label">Campos da proposta</label>
      {fields.map((f, i) => (
        <div className="row" key={i} style={{ gap: 6 }}>
          <input placeholder="campo" value={f.key} onChange={(e) => setField(i, { key: e.target.value })} />
          <select value={f.type} onChange={(e) => setField(i, { type: e.target.value as Field['type'] })} style={{ width: 110 }}>
            <option value="number">número</option>
            <option value="text">texto</option>
            <option value="boolean">booleano</option>
            <option value="date">data</option>
          </select>
          {f.type === 'boolean' ? (
            <select value={f.value} onChange={(e) => setField(i, { value: e.target.value })}>
              <option value="true">verdadeiro</option>
              <option value="false">falso</option>
            </select>
          ) : (
            <input
              placeholder={f.type === 'date' ? 'aaaa-mm-dd' : 'valor'}
              value={f.value}
              onChange={(e) => setField(i, { value: e.target.value })}
            />
          )}
          <button className="btn btn--secondary btn--sm" onClick={() => setFields(fields.filter((_, j) => j !== i))}>×</button>
        </div>
      ))}
      <button className="btn btn--secondary btn--sm" onClick={() => setFields([...fields, { key: '', value: '', type: 'number' }])}>+ Campo</button>

      <button className="btn btn--primary" disabled={running} onClick={() => void run()}>
        {running ? 'Executando…' : 'Executar decisão'}
      </button>

      {error && <div className="error-box">{error}</div>}

      {result && (
        <div className="card">
          <div className="row spread">
            <strong className={`outcome ${result.outcome}`}>{result.outcome}</strong>
            <span className="muted">pontos: {result.score} · limite: {result.limit}</span>
          </div>
          {result.error && <div className="error-box">{result.error}</div>}

          {result.justifications.length > 0 && (
            <div style={{ marginTop: 8 }}>
              <div className="form-label">Justificativas</div>
              <ul style={{ margin: 0, paddingLeft: '1.1rem' }}>
                {result.justifications.map((j, i) => <li key={i} className="muted">{j}</li>)}
              </ul>
            </div>
          )}

          {Object.keys(result.outputs).length > 0 && (
            <div style={{ marginTop: 8 }}>
              <div className="form-label">Parâmetros de saída</div>
              {Object.entries(result.outputs).map(([k, v]) => (
                <div key={k} className="row spread" style={{ fontSize: '0.82rem' }}>
                  <code>{k}</code><span>{v}</span>
                </div>
              ))}
            </div>
          )}
          <div className="stack" style={{ marginTop: 8 }}>
            {result.trace.map((t) => (
              <div className="trace-step" key={t.sequence}>
                <div className="seq">#{t.sequence} · {t.nodeLabel}</div>
                {t.result && <div>{t.result}</div>}
                {t.message && <div className="muted">{t.message}</div>}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

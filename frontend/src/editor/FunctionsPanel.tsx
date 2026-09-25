import { FUNCTIONS } from './functionCatalog';

/** "Funções" section: read-only reference of the pt-BR formula functions. */
export function FunctionsPanel() {
  return (
    <div className="stack">
      <h3 style={{ margin: 0 }}>Funções</h3>
      <div className="hint">Funções disponíveis nas fórmulas (estilo Excel, em português).</div>
      {FUNCTIONS.map((f) => (
        <div key={f.name} className="card" style={{ padding: '0.5rem 0.7rem', marginBottom: '0.4rem' }}>
          <div style={{ fontFamily: 'ui-monospace, monospace', fontSize: '0.82rem', color: 'var(--cor-primaria)' }}>{f.signature}</div>
          <div className="muted" style={{ fontSize: '0.78rem' }}>{f.description}</div>
        </div>
      ))}
    </div>
  );
}

/** "Objeto de análise" section: placeholder to be specified later. */
export function AnalysisObjectPanel() {
  return (
    <div className="stack">
      <h3 style={{ margin: 0 }}>Objeto de análise</h3>
      <div className="card">
        <div className="muted">Seção a especificar. Aqui será definido o objeto de análise da política.</div>
      </div>
    </div>
  );
}

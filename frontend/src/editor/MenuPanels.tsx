import type { SourceDescriptorDto } from '../api/types';

/** Placeholder for Crivo features not yet implemented. */
export function ComingSoonPanel({ text }: { text: string }) {
  return (
    <div className="card" style={{ padding: '0.7rem 0.8rem' }}>
      <div className="muted">{text}</div>
      <span className="badge Draft" style={{ marginTop: 6 }}>em breve</span>
    </div>
  );
}

/** "Fontes de Informação": read-only catalog + how to reference in formulas. */
export function SourcesRefPanel({ sources }: { sources: SourceDescriptorDto[] }) {
  return (
    <div className="stack">
      <div className="hint">
        Use nas fórmulas com <code>[Fonte;Produto;Dado]</code>. As fontes aparecem
        conforme as integrações são implementadas.
      </div>
      {sources.length === 0 && <div className="muted">Nenhuma fonte disponível.</div>}
      {sources.map((s) => (
        <div key={s.name} className="card" style={{ padding: '0.6rem 0.7rem', marginBottom: '0.4rem' }}>
          <strong>{s.name}</strong>
          {s.products.map((p) => (
            <div key={p.name} style={{ marginTop: 4 }}>
              <div className="muted" style={{ fontSize: '0.78rem' }}>{p.name}</div>
              <div className="row" style={{ flexWrap: 'wrap', gap: 4, marginTop: 2 }}>
                {p.data.map((d) => (
                  <code
                    key={d.name}
                    style={{
                      background: 'var(--cor-fundo)', border: '1px solid var(--cor-borda)',
                      borderRadius: 'var(--radius-sm)', padding: '0.1rem 0.4rem', fontSize: '0.72rem',
                    }}
                  >
                    [{s.name};{p.name};{d.name}]
                  </code>
                ))}
              </div>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

interface OperatorDoc {
  symbol: string;
  description: string;
}

const OPERATORS: OperatorDoc[] = [
  { symbol: '+  -  *  /', description: 'Aritméticos' },
  { symbol: '^', description: 'Potência' },
  { symbol: '&', description: 'Concatenação de texto' },
  { symbol: '=  <>', description: 'Igual / diferente' },
  { symbol: '<  <=  >  >=', description: 'Comparações' },
  { symbol: ';', description: 'Separador de argumentos' },
];

/** "Operadores": read-only reference of the formula operators. */
export function OperatorsPanel() {
  return (
    <div className="stack">
      <div className="hint">Operadores disponíveis nas expressões.</div>
      {OPERATORS.map((o) => (
        <div key={o.symbol} className="row spread" style={{ padding: '0.3rem 0.2rem', borderBottom: '1px solid var(--cor-borda)' }}>
          <code style={{ fontSize: '0.82rem', color: 'var(--cor-primaria)' }}>{o.symbol}</code>
          <span className="muted" style={{ fontSize: '0.75rem' }}>{o.description}</span>
        </div>
      ))}
    </div>
  );
}

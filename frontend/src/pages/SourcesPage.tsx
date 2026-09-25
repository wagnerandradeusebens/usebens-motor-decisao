import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { SourceDescriptorDto } from '../api/types';

/**
 * Read-only list of registered external sources (integrations). Sources appear
 * here as integrations are implemented in the backend; users do not create them.
 */
export function SourcesPage() {
  const [sources, setSources] = useState<SourceDescriptorDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      try {
        setSources(await api.listSources());
      } catch (e) {
        setError(e instanceof ApiError ? e.message : 'Falha ao carregar as fontes.');
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  return (
    <div className="container">
      <div className="card__title">Fontes de dados</div>
      <p className="muted" style={{ marginBottom: '1rem' }}>
        As fontes aparecem aqui conforme as integrações são implementadas. Use-as nas
        fórmulas com a notação <code>[Fonte;Produto;Dado]</code>, ex.:{' '}
        <code>[SERASA;Score;Pontuacao]</code>.
      </p>

      {error && <div className="error-box">{error}</div>}

      {loading ? (
        <p className="muted">Carregando…</p>
      ) : sources.length === 0 ? (
        <p className="muted">Nenhuma integração disponível ainda.</p>
      ) : (
        sources.map((s) => (
          <div className="card" key={s.name}>
            <div className="row spread">
              <strong>{s.name}</strong>
              <span className="badge Published">integração</span>
            </div>
            {s.description && <div className="muted" style={{ marginBottom: '0.5rem' }}>{s.description}</div>}

            {s.products.map((p) => (
              <div key={p.name} style={{ marginTop: '0.5rem' }}>
                <div style={{ fontWeight: 600 }}>{p.name}</div>
                {p.description && <div className="muted">{p.description}</div>}
                <div className="row" style={{ flexWrap: 'wrap', gap: '0.4rem', marginTop: '0.35rem' }}>
                  {p.data.map((d) => (
                    <code
                      key={d.name}
                      title={d.description ?? undefined}
                      style={{
                        background: 'var(--cor-fundo)',
                        border: '1px solid var(--cor-borda)',
                        borderRadius: 'var(--radius-sm)',
                        padding: '0.15rem 0.45rem',
                        fontSize: '0.8rem',
                      }}
                    >
                      [{s.name};{p.name};{d.name}]
                    </code>
                  ))}
                </div>
              </div>
            ))}
          </div>
        ))
      )}
    </div>
  );
}

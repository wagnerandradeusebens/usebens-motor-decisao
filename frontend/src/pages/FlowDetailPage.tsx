import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FlowSummary } from '../api/types';

/** Shows a flow's versions and lets the user create/open versions. */
export function FlowDetailPage() {
  const { flowId } = useParams();
  const [flow, setFlow] = useState<FlowSummary | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    if (!flowId) return;
    try {
      setFlow(await api.getFlow(flowId));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao carregar a política.');
    }
  }

  useEffect(() => {
    void load();
  }, [flowId]);

  async function newVersion(copyFrom?: string) {
    if (!flowId) return;
    try {
      await api.createVersion(flowId, copyFrom);
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao criar versão.');
    }
  }

  if (!flow) {
    return <div className="container">{error ? <div className="error-box">{error}</div> : <p className="muted">Carregando…</p>}</div>;
  }

  return (
    <div className="container">
      <div className="row spread">
        <div>
          <h2 style={{ marginBottom: 4 }}>{flow.name}</h2>
          {flow.description && <div className="muted">{flow.description}</div>}
        </div>
        <button className="btn btn--primary" onClick={() => void newVersion()}>Nova versão</button>
      </div>

      {error && <div className="error-box">{error}</div>}

      <div className="card__title" style={{ marginTop: '1rem' }}>Versões</div>
      {flow.versions.map((v) => (
        <div className="card" key={v.id}>
          <div className="row spread">
            <div className="row" style={{ gap: 10 }}>
              <strong>v{v.versionNumber}</strong>
              <span className={`badge ${v.status}`}>{v.status}</span>
              {v.publishedAt && <span className="muted">publicada {new Date(v.publishedAt).toLocaleString('pt-BR')}</span>}
            </div>
            <div className="row" style={{ gap: 8 }}>
              <Link to={`/flows/${flow.id}/versions/${v.id}`}>
                <button className="btn btn--primary btn--sm">{v.status === 'Draft' ? 'Editar' : 'Abrir'}</button>
              </Link>
              <button className="btn btn--secondary btn--sm" onClick={() => void newVersion(v.id)}>Duplicar</button>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

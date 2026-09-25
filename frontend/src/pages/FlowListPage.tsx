import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FlowSummary } from '../api/types';
import { Modal } from '../editor/VariablesPanel';

/** Formats an ISO date to pt-BR, or an em dash when absent. */
function fmt(date: string | null | undefined): string {
  return date ? new Date(date).toLocaleString('pt-BR') : '—';
}

/** A flow is "published" when it has any published version. */
function isPublished(f: FlowSummary): boolean {
  return f.versions.some((v) => v.status === 'Published');
}

/**
 * Políticas page: a larger top area with the list of policies (one row each,
 * with a published indicator), and a smaller bottom area showing the selected
 * policy's history (versions with status and dates). "Nova política" is a button
 * that opens a create modal.
 */
export function FlowListPage() {
  const navigate = useNavigate();
  const [flows, setFlows] = useState<FlowSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  async function load() {
    setLoading(true);
    try {
      const list = await api.listFlows();
      setFlows(list);
      // Keep selection valid.
      setSelectedId((cur) => (cur && list.some((f) => f.id === cur) ? cur : list[0]?.id ?? null));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao carregar políticas.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  const selected = useMemo(() => flows.find((f) => f.id === selectedId) ?? null, [flows, selectedId]);

  async function create() {
    setError(null);
    try {
      const flow = await api.createFlow(name.trim(), description.trim() || null);
      setCreating(false);
      setName('');
      setDescription('');
      await load();
      // Jump straight into editing the new policy's first version.
      const v = flow.versions[0];
      if (v) navigate(`/flows/${flow.id}/versions/${v.id}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao criar política.');
    }
  }

  async function remove(id: string, flowName: string) {
    if (!window.confirm(`Excluir a política "${flowName}"? Esta ação é irreversível e remove todas as versões e execuções.`)) {
      return;
    }
    setError(null);
    try {
      await api.deleteFlow(id);
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao excluir a política.');
    }
  }

  return (
    <div className="container" style={{ display: 'flex', flexDirection: 'column', gap: '1rem', height: 'calc(100vh - var(--topbar-height) - 3rem)' }}>
      <div className="row spread">
        <h2 style={{ margin: 0 }}>Políticas</h2>
        <button className="btn btn--primary" onClick={() => setCreating(true)}>+ Nova política</button>
      </div>

      {error && <div className="error-box">{error}</div>}

      {/* Top area: list of policies (larger) */}
      <div className="card" style={{ flex: 2, overflowY: 'auto', padding: 0 }}>
        {loading ? (
          <p className="muted" style={{ padding: '1rem' }}>Carregando…</p>
        ) : flows.length === 0 ? (
          <p className="muted" style={{ padding: '1rem' }}>Nenhuma política ainda. Crie a primeira no botão acima.</p>
        ) : (
          flows.map((f) => {
            const published = isPublished(f);
            const active = f.id === selectedId;
            return (
              <div
                key={f.id}
                onClick={() => setSelectedId(f.id)}
                style={{
                  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                  padding: '0.75rem 1rem', borderBottom: '1px solid var(--cor-borda)',
                  cursor: 'pointer', background: active ? 'var(--cor-fundo)' : 'transparent',
                }}
              >
                <div className="row" style={{ gap: 10 }}>
                  <span
                    title={published ? 'Publicada' : 'Sem versão publicada'}
                    style={{
                      width: 10, height: 10, borderRadius: '50%',
                      background: published ? 'var(--cor-positivo)' : 'var(--marca-cinza)',
                      flexShrink: 0,
                    }}
                  />
                  <div>
                    <strong>{f.name}</strong>
                    {f.description && <div className="muted">{f.description}</div>}
                  </div>
                </div>
                <div className="row" style={{ gap: 12 }}>
                  <span className={`badge ${published ? 'Published' : 'Draft'}`}>
                    {published ? 'Publicada' : 'Rascunho'}
                  </span>
                  <span className="muted">{f.versions.length} versão(ões)</span>
                  <button
                    className="btn btn--danger btn--sm"
                    onClick={(e) => { e.stopPropagation(); void remove(f.id, f.name); }}
                  >
                    Excluir
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Bottom area: selected policy history (smaller) */}
      <div className="card" style={{ flex: 1, overflowY: 'auto' }}>
        <div className="row spread">
          <div className="card__title">Histórico {selected ? `· ${selected.name}` : ''}</div>
          {selected && <Link to={`/flows/${selected.id}/execucoes`}>Ver execuções →</Link>}
        </div>
        {!selected ? (
          <div className="muted">Selecione uma política acima para ver o histórico.</div>
        ) : selected.versions.length === 0 ? (
          <div className="muted">Sem versões.</div>
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--cor-texto-claro)' }}>
                <th style={{ padding: '0.4rem 0.5rem' }}>Versão</th>
                <th style={{ padding: '0.4rem 0.5rem' }}>Status</th>
                <th style={{ padding: '0.4rem 0.5rem' }}>Criada</th>
                <th style={{ padding: '0.4rem 0.5rem' }}>Atualizada</th>
                <th style={{ padding: '0.4rem 0.5rem' }}>Publicada</th>
                <th style={{ padding: '0.4rem 0.5rem' }}></th>
              </tr>
            </thead>
            <tbody>
              {[...selected.versions]
                .sort((a, b) => b.versionNumber - a.versionNumber)
                .map((v) => (
                  <tr key={v.id} style={{ borderTop: '1px solid var(--cor-borda)' }}>
                    <td style={{ padding: '0.4rem 0.5rem' }}>v{v.versionNumber}</td>
                    <td style={{ padding: '0.4rem 0.5rem' }}><span className={`badge ${v.status}`}>{v.status}</span></td>
                    <td style={{ padding: '0.4rem 0.5rem' }}>{fmt(v.createdAt)}</td>
                    <td style={{ padding: '0.4rem 0.5rem' }}>{fmt(v.updatedAt)}</td>
                    <td style={{ padding: '0.4rem 0.5rem' }}>{fmt(v.publishedAt)}</td>
                    <td style={{ padding: '0.4rem 0.5rem' }}>
                      <Link to={`/flows/${selected.id}/versions/${v.id}`}>
                        {v.status === 'Draft' ? 'Editar' : 'Abrir'}
                      </Link>
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        )}
      </div>

      {creating && (
        <Modal title="Nova política" onClose={() => setCreating(false)}>
          <div className="form-group">
            <label className="form-label">Nome</label>
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex.: Crédito auto - pessoa física" />
          </div>
          <div className="form-group">
            <label className="form-label">Descrição</label>
            <input value={description} onChange={(e) => setDescription(e.target.value)} placeholder="Opcional" />
          </div>
          <div className="row" style={{ justifyContent: 'flex-end', gap: 8 }}>
            <button className="btn btn--secondary" onClick={() => setCreating(false)}>Cancelar</button>
            <button className="btn btn--primary" disabled={!name.trim()} onClick={() => void create()}>Criar</button>
          </div>
        </Modal>
      )}
    </div>
  );
}

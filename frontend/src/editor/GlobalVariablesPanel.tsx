import { useEffect, useState } from 'react';
import type { GlobalVariable, SourceDescriptorDto } from '../api/types';
import { api, ApiError } from '../api/client';
import { FormulaInput } from './FormulaInput';
import { Modal } from './VariablesPanel';

interface Props {
  fields: string[];
  sources: SourceDescriptorDto[];
  /** Notifies the editor when the set of global variables changes (for autocomplete). */
  onChange?: (variables: GlobalVariable[]) => void;
}

/**
 * "Variáveis Globais" section: reusable formulas shared across every policy.
 * Unlike local variables (kept in the version graph and saved with it), globals
 * are persisted immediately through their own API and take effect for all flows.
 */
export function GlobalVariablesPanel({ fields, sources, onChange }: Props) {
  const [items, setItems] = useState<GlobalVariable[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [editing, setEditing] = useState<GlobalVariable | 'new' | null>(null);
  const [draftKey, setDraftKey] = useState('');
  const [draftExpr, setDraftExpr] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const list = await api.listGlobalVariables();
        setItems(list);
        onChange?.(list);
      } catch (e) {
        setError(e instanceof ApiError ? e.message : 'Falha ao carregar variáveis globais.');
      } finally {
        setLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function refresh(list: GlobalVariable[]) {
    setItems(list);
    onChange?.(list);
  }

  function openNew() {
    setEditing('new');
    setDraftKey('');
    setDraftExpr('');
    setError(null);
  }

  function openEdit(v: GlobalVariable) {
    setEditing(v);
    setDraftKey(v.key);
    setDraftExpr(v.expression);
    setError(null);
  }

  async function save() {
    const key = draftKey.trim();
    if (!key) return;
    setSaving(true);
    setError(null);
    try {
      if (editing === 'new') {
        const created = await api.createGlobalVariable(key, key, draftExpr);
        refresh([...items, created].sort((a, b) => a.key.localeCompare(b.key)));
      } else if (editing) {
        const updated = await api.updateGlobalVariable(editing.id, key, key, draftExpr);
        refresh(items.map((v) => (v.id === updated.id ? updated : v)).sort((a, b) => a.key.localeCompare(b.key)));
      }
      setEditing(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao salvar a variável global.');
    } finally {
      setSaving(false);
    }
  }

  async function remove(v: GlobalVariable) {
    setError(null);
    try {
      await api.deleteGlobalVariable(v.id);
      refresh(items.filter((x) => x.id !== v.id));
      setEditing(null);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao excluir a variável global.');
    }
  }

  const otherKeys = items
    .filter((v) => editing === 'new' || (editing && v.id !== editing.id))
    .map((v) => v.key);

  return (
    <div className="stack">
      <div className="row spread">
        <h3 style={{ margin: 0 }}>Variáveis Globais</h3>
        <button className="btn btn--secondary btn--sm" onClick={openNew}>+ Variável global</button>
      </div>
      <div className="muted" style={{ fontSize: '0.78rem' }}>
        Compartilhadas entre todas as políticas. Uma variável local de mesmo nome tem prioridade.
      </div>

      {error && !editing && <div className="error-box">{error}</div>}

      {loading ? (
        <div className="muted">Carregando…</div>
      ) : items.length === 0 ? (
        <div className="muted">Nenhuma variável global criada.</div>
      ) : (
        items.map((v) => (
          <div className="card" key={v.id} style={{ padding: '0.6rem 0.8rem', marginBottom: '0.5rem' }}>
            <div className="row spread">
              <strong style={{ fontFamily: 'ui-monospace, monospace' }}>{v.key}</strong>
              <div className="row" style={{ gap: 6 }}>
                <button className="btn btn--secondary btn--sm" onClick={() => openEdit(v)}>Editar</button>
                <button className="btn btn--secondary btn--sm" title="Excluir variável global" onClick={() => void remove(v)}>×</button>
              </div>
            </div>
            <div className="muted" style={{ fontFamily: 'ui-monospace, monospace', fontSize: '0.78rem', marginTop: 4 }}>
              = {v.expression || <span style={{ fontStyle: 'italic' }}>(vazia)</span>}
            </div>
          </div>
        ))
      )}

      {editing !== null && (
        <Modal title={editing === 'new' ? 'Nova variável global' : 'Editar variável global'} onClose={() => setEditing(null)}>
          {error && <div className="error-box" style={{ marginBottom: '0.75rem' }}>{error}</div>}
          <div className="form-group">
            <label className="form-label">Nome da variável</label>
            <input value={draftKey} onChange={(e) => setDraftKey(e.target.value)} placeholder="ex.: idade_minima" />
          </div>
          <div className="form-group">
            <label className="form-label">Fórmula</label>
            <FormulaInput
              value={draftExpr}
              onChange={setDraftExpr}
              fields={fields}
              variables={otherKeys}
              sources={sources}
              rows={4}
              placeholder="ex.: 18   ou   ARRED('renda' * 0.3; 2)"
            />
            <div className="hint">
              Campos <code>'campo'</code>, variáveis <code>{'{variavel}'}</code>, texto{' '}
              <code>"texto"</code>, fontes <code>[Fonte;Produto;Dado]</code> e funções (SE, ARRED…).
              Separador: <code>;</code>
            </div>
          </div>
          <div className="row spread" style={{ gap: 8 }}>
            {editing !== 'new' ? (
              <button className="btn btn--danger" onClick={() => void remove(editing)}>Excluir</button>
            ) : (
              <span />
            )}
            <div className="row" style={{ gap: 8 }}>
              <button className="btn btn--secondary" onClick={() => setEditing(null)}>Fechar</button>
              <button className="btn btn--primary" onClick={() => void save()} disabled={saving || !draftKey.trim()}>
                {saving ? 'Salvando…' : 'Salvar'}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

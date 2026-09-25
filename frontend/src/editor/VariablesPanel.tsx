import { useState } from 'react';
import type { GraphFormula, SourceDescriptorDto } from '../api/types';
import { FormulaInput } from './FormulaInput';

interface Props {
  variables: GraphFormula[];
  fields: string[];
  sources: SourceDescriptorDto[];
  readOnly: boolean;
  onChange: (variables: GraphFormula[]) => void;
}

/**
 * "Variáveis" section: the named formulas the user creates for use in the flow.
 * Each is persisted as a backend Formula (key = name, expression). Editing opens
 * a modal with a name field and the autocomplete formula editor.
 */
export function VariablesPanel({ variables, fields, sources, readOnly, onChange }: Props) {
  const [editing, setEditing] = useState<number | null>(null);
  const [draftName, setDraftName] = useState('');
  const [draftExpr, setDraftExpr] = useState('');

  function openNew() {
    setEditing(-1);
    setDraftName('');
    setDraftExpr('');
  }

  function openEdit(i: number) {
    setEditing(i);
    setDraftName(variables[i].key);
    setDraftExpr(variables[i].expression);
  }

  function save() {
    const name = draftName.trim();
    if (!name) return;
    const entry: GraphFormula = { key: name, label: name, expression: draftExpr };
    if (editing === -1) {
      onChange([...variables, entry]);
    } else if (editing !== null) {
      const next = [...variables];
      next[editing] = entry;
      onChange(next);
    }
    setEditing(null);
  }

  function remove(i: number) {
    onChange(variables.filter((_, j) => j !== i));
    setEditing(null);
  }

  // Inside the modal a variable can reference request fields ('campo'), other
  // variables ({outra}), functions and sources ([Fonte;Produto;Dado]).
  const otherVariables = variables.filter((_, i) => i !== editing).map((v) => v.key);

  return (
    <div className="stack">
      <div className="row spread">
        <h3 style={{ margin: 0 }}>Variáveis</h3>
        {!readOnly && <button className="btn btn--secondary btn--sm" onClick={openNew}>+ Variável</button>}
      </div>

      {variables.length === 0 && <div className="muted">Nenhuma variável criada.</div>}

      {variables.map((v, i) => (
        <div className="card" key={v.key} style={{ padding: '0.6rem 0.8rem', marginBottom: '0.5rem' }}>
          <div className="row spread">
            <strong style={{ fontFamily: 'ui-monospace, monospace' }}>{v.key}</strong>
            {!readOnly && (
              <div className="row" style={{ gap: 6 }}>
                <button className="btn btn--secondary btn--sm" onClick={() => openEdit(i)}>Editar</button>
                <button className="btn btn--secondary btn--sm" title="Excluir variável" onClick={() => remove(i)}>×</button>
              </div>
            )}
          </div>
          <div className="muted" style={{ fontFamily: 'ui-monospace, monospace', fontSize: '0.78rem', marginTop: 4 }}>
            = {v.expression || <span style={{ fontStyle: 'italic' }}>(vazia)</span>}
          </div>
        </div>
      ))}

      {editing !== null && (
        <Modal title={editing === -1 ? 'Nova variável' : 'Editar variável'} onClose={() => setEditing(null)}>
          <div className="form-group">
            <label className="form-label">Nome da variável</label>
            <input
              value={draftName}
              onChange={(e) => setDraftName(e.target.value)}
              placeholder="ex.: comprometimento"
              disabled={readOnly}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Fórmula</label>
            <FormulaInput
              value={draftExpr}
              onChange={setDraftExpr}
              fields={fields}
              variables={otherVariables}
              sources={sources}
              rows={4}
              disabled={readOnly}
              placeholder="ex.: 'divida' / 'renda'   ou   [SERASA;Score;Pontuacao]"
            />
            <div className="hint">
              Campos <code>'campo'</code>, variáveis <code>{'{variavel}'}</code>, texto{' '}
              <code>"texto"</code>, fontes <code>[Fonte;Produto;Dado]</code> e funções (SE, ARRED…).
              Separador de argumentos: <code>;</code>
            </div>
          </div>
          <div className="row spread" style={{ gap: 8 }}>
            {editing !== null && editing >= 0 && !readOnly ? (
              <button className="btn btn--danger" onClick={() => remove(editing)}>Excluir</button>
            ) : (
              <span />
            )}
            <div className="row" style={{ gap: 8 }}>
              <button className="btn btn--secondary" onClick={() => setEditing(null)}>Fechar</button>
              <button className="btn btn--primary" onClick={save} disabled={readOnly || !draftName.trim()}>Salvar</button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

/** Minimal modal overlay. */
export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          background: 'var(--cor-branco)', borderRadius: 'var(--radius-md)',
          padding: '1.25rem', width: 'min(560px, 92vw)', maxHeight: '88vh', overflowY: 'auto',
          boxShadow: 'var(--sombra-elevada)',
        }}
      >
        <div className="card__title" style={{ marginBottom: '1rem' }}>{title}</div>
        {children}
      </div>
    </div>
  );
}

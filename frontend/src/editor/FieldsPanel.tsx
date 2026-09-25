import type { GraphInputField, InputFieldType } from '../api/types';

const TYPES: InputFieldType[] = ['Number', 'Text', 'Boolean', 'Date'];
const TYPE_LABEL: Record<InputFieldType, string> = {
  Number: 'número',
  Text: 'texto',
  Boolean: 'booleano',
  Date: 'data',
};

interface Props {
  fields: GraphInputField[];
  readOnly: boolean;
  onChange: (fields: GraphInputField[]) => void;
}

/** "Campos" section: declares the fields that arrive in the request (the proposal). */
export function FieldsPanel({ fields, readOnly, onChange }: Props) {
  function add() {
    onChange([
      ...fields,
      { name: '', label: '', type: 'Number', required: false, order: fields.length + 1 },
    ]);
  }
  function update(i: number, patch: Partial<GraphInputField>) {
    const next = [...fields];
    next[i] = { ...next[i], ...patch };
    onChange(next);
  }
  function remove(i: number) {
    onChange(fields.filter((_, j) => j !== i));
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h3 style={{ margin: 0 }}>Campos de entrada</h3>
        {!readOnly && <button className="btn btn--secondary btn--sm" onClick={add}>+ Campo</button>}
      </div>
      <div className="hint">Campos que chegam na requisição (a proposta). Usados no portal de execução e no autocomplete.</div>

      {fields.length === 0 && <div className="muted">Nenhum campo declarado.</div>}

      {fields.map((f, i) => (
        <div className="card" key={i} style={{ padding: '0.6rem 0.8rem', marginBottom: '0.5rem' }}>
          <div className="stack" style={{ gap: '0.4rem' }}>
            <input disabled={readOnly} placeholder="nome técnico (ex.: renda_mensal)" value={f.name} onChange={(e) => update(i, { name: e.target.value })} />
            <input disabled={readOnly} placeholder="rótulo (ex.: Renda mensal)" value={f.label} onChange={(e) => update(i, { label: e.target.value })} />
            <div className="row" style={{ gap: 8 }}>
              <select disabled={readOnly} value={f.type} onChange={(e) => update(i, { type: e.target.value as InputFieldType })}>
                {TYPES.map((t) => <option key={t} value={t}>{TYPE_LABEL[t]}</option>)}
              </select>
              <label className="row" style={{ gap: 4, fontSize: '0.82rem', whiteSpace: 'nowrap' }}>
                <input type="checkbox" disabled={readOnly} style={{ width: 'auto' }} checked={f.required} onChange={(e) => update(i, { required: e.target.checked })} />
                obrigatório
              </label>
              {!readOnly && <button className="btn btn--secondary btn--sm" title="Excluir campo" onClick={() => remove(i)}>×</button>}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

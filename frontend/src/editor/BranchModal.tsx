import { useState } from 'react';
import type { DecisionOutcome } from '../api/types';
import { Modal } from './VariablesPanel';

export type BranchTarget =
  | { kind: 'node'; nodeKey: string }
  | { kind: 'outcome'; outcome: DecisionOutcome };

const OUTCOMES: { value: DecisionOutcome; label: string }[] = [
  { value: 'Approved', label: 'Aprovar' },
  { value: 'ApprovedWithConditions', label: 'Aprovar com condições' },
  { value: 'ManualReview', label: 'Revisão manual' },
  { value: 'Denied', label: 'Negar' },
];

interface Props {
  branch: 'true' | 'false';
  nodeOptions: { key: string; label: string }[];
  onCancel: () => void;
  onConfirm: (target: BranchTarget) => void;
}

/**
 * Modal to define what a Condition's true/false branch does: either go to an
 * existing node, or reach a terminal decision outcome (which the editor turns
 * into a Decision node + edge so the backend graph stays valid).
 */
export function BranchModal({ branch, nodeOptions, onCancel, onConfirm }: Props) {
  const [mode, setMode] = useState<'outcome' | 'node'>('outcome');
  const [outcome, setOutcome] = useState<DecisionOutcome>('Approved');
  const [nodeKey, setNodeKey] = useState<string>(nodeOptions[0]?.key ?? '');

  const title = branch === 'true' ? 'Saída: Verdadeiro' : 'Saída: Falso';

  return (
    <Modal title={title} onClose={onCancel}>
      <div className="stack">
        <div className="tabs">
          <button className={`tab ${mode === 'outcome' ? 'active' : ''}`} onClick={() => setMode('outcome')}>
            Desfecho
          </button>
          <button className={`tab ${mode === 'node' ? 'active' : ''}`} onClick={() => setMode('node')} disabled={nodeOptions.length === 0}>
            Ir para nó
          </button>
        </div>

        {mode === 'outcome' ? (
          <div className="form-group">
            <label className="form-label">Desfecho desta saída</label>
            <select value={outcome} onChange={(e) => setOutcome(e.target.value as DecisionOutcome)}>
              {OUTCOMES.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>
        ) : (
          <div className="form-group">
            <label className="form-label">Nó de destino</label>
            <select value={nodeKey} onChange={(e) => setNodeKey(e.target.value)}>
              {nodeOptions.map((n) => (
                <option key={n.key} value={n.key}>{n.label}</option>
              ))}
            </select>
          </div>
        )}

        <div className="row" style={{ justifyContent: 'flex-end', gap: 8 }}>
          <button className="btn btn--secondary" onClick={onCancel}>Cancelar</button>
          <button
            className="btn btn--primary"
            onClick={() =>
              onConfirm(mode === 'outcome' ? { kind: 'outcome', outcome } : { kind: 'node', nodeKey })
            }
            disabled={mode === 'node' && !nodeKey}
          >
            Confirmar
          </button>
        </div>
      </div>
    </Modal>
  );
}

import type { DecisionOutcome, FlowNodeKind } from '../api/models';

/** Itens arrastáveis da paleta (menu estilo Crivo, seção "Regra"). */
export const RULE_PALETTE: { kind: FlowNodeKind; label: string }[] = [
  { kind: 'Condition', label: 'Nova Regra (condição)' },
  { kind: 'Matrix', label: 'Regra Matriz' },
  { kind: 'Ruleset', label: 'Conjunto de regras' },
  { kind: 'Computation', label: 'Cálculo' },
  { kind: 'DataSource', label: 'Fonte de dados' },
  { kind: 'Action', label: 'Ação' },
  { kind: 'Decision', label: 'Decisão' },
  { kind: 'Comment', label: 'Comentário' },
];

export const OUTCOME_LABEL: Record<DecisionOutcome, string> = {
  Pending: 'Pendente',
  Approved: 'Aprovar',
  ApprovedWithConditions: 'Aprovar c/ condições',
  ManualReview: 'Revisão manual',
  Denied: 'Negar',
};

/** JSON de config inicial por tipo de nó (espelha defaultConfig do React). */
export function defaultConfig(kind: FlowNodeKind): string {
  switch (kind) {
    case 'Condition':
      return JSON.stringify({ expression: '' });
    case 'Computation':
      return JSON.stringify({ assignments: [] });
    case 'Decision':
      return JSON.stringify({ outcome: 'Approved', message: '' });
    case 'DataSource':
      return JSON.stringify({ source: '' });
    case 'Action':
      return JSON.stringify({ actions: [] });
    case 'Matrix':
      return JSON.stringify({
        mode: 'Points', rowExpression: '', colExpression: '',
        rowBands: [], colBands: [], cells: [], defaultValue: '0',
      });
    case 'Comment':
      return JSON.stringify({ text: '' });
    default:
      return '{}';
  }
}

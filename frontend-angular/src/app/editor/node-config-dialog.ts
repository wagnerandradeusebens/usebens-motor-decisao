import { Component, Inject, signal, WritableSignal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import type {
  ActionsConfig,
  ComputationConfig,
  DecisionConfig,
  DecisionOutcome,
  FlowNodeKind,
  MatrixBand,
  MatrixConfig,
  MatrixMode,
  SourceDescriptorDto,
} from '../api/models';
import { FormulaInput } from './formula-input';

export interface NodeConfigData {
  kind: FlowNodeKind;
  label: string;
  config: string;
  readOnly: boolean;
  /** Listas para o autocomplete de fórmula (mesmas do editor de variáveis). */
  fields: string[];
  variables: string[];
  sources: SourceDescriptorDto[];
  policies: string[];
  /** Variáveis por política (para o 3º nível da referência cruzada). */
  policyVariables: Record<string, string[]>;
  /** Tabelas (nome + colunas) para o autocomplete de PROCV. */
  tables: { name: string; columns: string[] }[];
}

/** Uma ação no editor (tipo + expressão; nome só para SetOutput). */
export interface ActionRow {
  type: string;
  name?: string | null;
  expression: string;
}

export interface NodeConfigResult {
  label: string;
  config: string;
  deleted?: boolean;
  /** Pede ao editor para abrir o diálogo de destino de uma saída da condição. */
  configureBranch?: 'true' | 'false';
}

const OUTCOMES: DecisionOutcome[] = ['Approved', 'ApprovedWithConditions', 'ManualReview', 'Denied'];
const FORMULA_HINT =
  "Campos 'campo', variáveis {variavel}, texto \"texto\", fontes [Fonte;Produto;Dado], política $[Política;Categoria;Variável], tabelas PROCV(\"tabela\";\"coluna\";chave), funções (SE, ARRED…). Separador: ;";

/**
 * Diálogo de configuração de um nó, por tipo. Cobre Condition, Computation,
 * Decision, DataSource, Comment e Action com editores dedicados; para tipos com
 * config mais rica (Matrix, Ruleset) oferece um editor de JSON cru como fallback,
 * garantindo que nenhuma configuração fique inacessível.
 */
@Component({
  selector: 'app-node-config-dialog',
  imports: [
    NgTemplateOutlet,
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
    FormulaInput,
  ],
  templateUrl: './node-config-dialog.html',
  styleUrl: './node-config-dialog.scss',
})
export class NodeConfigDialog {
  protected readonly outcomes = OUTCOMES;
  protected readonly hint = FORMULA_HINT;

  protected label = '';
  /** Estado parseado da config (por tipo). */
  protected expression = ''; // Condition
  protected assignments = signal<{ targetField: string; expression: string }[]>([]); // Computation
  protected outcome: DecisionOutcome = 'Approved'; // Decision
  protected message = ''; // Decision
  protected source = ''; // DataSource
  protected text = ''; // Comment
  protected actions = signal<ActionRow[]>([]); // Action (nó legado)
  // Ações anexadas a qualquer nó: um conjunto para nós simples; V/F para condição.
  protected nodeActions = signal<ActionRow[]>([]); // Decision/Computation/DataSource
  protected trueActions = signal<ActionRow[]>([]); // Condition (ramo verdadeiro)
  protected falseActions = signal<ActionRow[]>([]); // Condition (ramo falso)
  protected matrix = signal<MatrixConfig>({
    mode: 'Points', rowExpression: '', colExpression: '', rowBands: [], colBands: [], cells: [], defaultValue: '0',
  }); // Matrix
  protected rawJson = ''; // fallback (Ruleset/outros)

  protected readonly actionTypes = [
    { value: 'AddPoints', label: 'Adiciona aos pontos' },
    { value: 'SetPoints', label: 'Define pontos' },
    { value: 'AddLimit', label: 'Adiciona ao limite' },
    { value: 'SetLimit', label: 'Define limite' },
    { value: 'AddJustification', label: 'Adiciona à justificativa' },
    { value: 'SetJustification', label: 'Define justificativa' },
    { value: 'SetOutput', label: 'Define parâmetro de saída' },
    { value: 'SetResposta', label: 'Define resposta' },
  ];

  constructor(
    private readonly ref: MatDialogRef<NodeConfigDialog, NodeConfigResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: NodeConfigData,
  ) {
    this.label = data.label;
    this.parse();
  }

  protected get kind(): FlowNodeKind {
    return this.data.kind;
  }
  protected get readOnly(): boolean {
    return this.data.readOnly;
  }

  private parse(): void {
    const cfg = safeParse(this.data.config);
    switch (this.data.kind) {
      case 'Condition': {
        const c = cfg as { expression?: string; trueActions?: ActionRow[]; falseActions?: ActionRow[] };
        this.expression = c.expression ?? '';
        this.trueActions.set(c.trueActions ?? []);
        this.falseActions.set(c.falseActions ?? []);
        break;
      }
      case 'Computation': {
        const c = cfg as ComputationConfig & { actions?: ActionRow[] };
        this.assignments.set(c.assignments ?? []);
        this.nodeActions.set(c.actions ?? []);
        break;
      }
      case 'Decision': {
        const c = cfg as DecisionConfig & { actions?: ActionRow[] };
        this.outcome = c.outcome ?? 'Approved';
        this.message = c.message ?? '';
        this.nodeActions.set(c.actions ?? []);
        break;
      }
      case 'DataSource': {
        const c = cfg as { source?: string; actions?: ActionRow[] };
        this.source = c.source ?? '';
        this.nodeActions.set(c.actions ?? []);
        break;
      }
      case 'Comment':
        this.text = (cfg as { text?: string }).text ?? '';
        break;
      case 'Action':
        this.actions.set((cfg as ActionsConfig).actions ?? []);
        break;
      case 'Matrix': {
        const m = cfg as MatrixConfig;
        this.matrix.set({
          mode: m.mode ?? 'Points',
          rowExpression: m.rowExpression ?? '',
          colExpression: m.colExpression ?? '',
          rowBands: m.rowBands ?? [],
          colBands: m.colBands ?? [],
          cells: m.cells ?? [],
          defaultValue: m.defaultValue ?? '0',
        });
        break;
      }
      default:
        this.rawJson = JSON.stringify(cfg, null, 2);
    }
  }

  // --- Computation helpers ---
  protected addAssignment(): void {
    this.assignments.update((a) => [...a, { targetField: '', expression: '' }]);
  }
  protected removeAssignment(i: number): void {
    this.assignments.update((a) => a.filter((_, j) => j !== i));
  }
  protected setAssignment(i: number, patch: Partial<{ targetField: string; expression: string }>): void {
    this.assignments.update((a) => a.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  }

  // --- Action helpers (genéricos: operam sobre o signal de ações informado) ---
  protected addAction(target: WritableSignal<ActionRow[]>): void {
    target.update((a) => [...a, { type: 'AddPoints', expression: '' }]);
  }
  protected removeAction(target: WritableSignal<ActionRow[]>, i: number): void {
    target.update((a) => a.filter((_, j) => j !== i));
  }
  protected setAction(target: WritableSignal<ActionRow[]>, i: number, patch: Partial<ActionRow>): void {
    target.update((a) => a.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  }

  // --- Matrix helpers ---
  private resizeCells(rows: number, cols: number, cells: string[][], mode: MatrixMode): string[][] {
    const fill = mode === 'Decision' ? 'ManualReview' : '0';
    return Array.from({ length: rows }, (_, r) =>
      Array.from({ length: cols }, (_, c) => cells[r]?.[c] ?? fill),
    );
  }
  protected setMatrixMode(mode: MatrixMode): void {
    this.matrix.update((m) => ({ ...m, mode }));
  }
  protected setMatrixExpr(which: 'rowExpression' | 'colExpression', value: string): void {
    this.matrix.update((m) => ({ ...m, [which]: value }));
  }
  protected addBand(which: 'rowBands' | 'colBands'): void {
    this.setBands(which, [...this.matrix()[which], { label: '', min: null, max: null }]);
  }
  protected removeBand(which: 'rowBands' | 'colBands', i: number): void {
    this.setBands(which, this.matrix()[which].filter((_, j) => j !== i));
  }
  protected patchBand(which: 'rowBands' | 'colBands', i: number, patch: Partial<MatrixBand>): void {
    this.setBands(which, this.matrix()[which].map((b, j) => (j === i ? { ...b, ...patch } : b)));
  }
  /** Define o valor padrão da matriz (usado quando o cruzamento não casa). */
  protected setDefaultValue(value: string): void {
    this.matrix.update((m) => ({ ...m, defaultValue: value }));
  }
  /**
   * Descrição legível do intervalo de uma faixa, refletindo a regra do motor
   * (min ≤ valor < max; vazio = aberto). Ex.: "2000 ≤ v < 5000", "v < 500",
   * "v ≥ 700", "qualquer valor".
   */
  protected bandRangeHint(b: MatrixBand): string {
    const hasMin = b.min !== null && b.min !== undefined;
    const hasMax = b.max !== null && b.max !== undefined;
    if (hasMin && hasMax) return `${b.min} ≤ v < ${b.max}`;
    if (hasMin) return `v ≥ ${b.min}`;
    if (hasMax) return `v < ${b.max}`;
    return 'qualquer valor';
  }
  private setBands(which: 'rowBands' | 'colBands', bands: MatrixBand[]): void {
    const m = this.matrix();
    const rows = which === 'rowBands' ? bands.length : m.rowBands.length;
    const cols = which === 'colBands' ? bands.length : m.colBands.length;
    this.matrix.set({ ...m, [which]: bands, cells: this.resizeCells(rows, cols, m.cells, m.mode) } as MatrixConfig);
  }
  protected setCell(r: number, c: number, value: string): void {
    const m = this.matrix();
    const cells = this.resizeCells(m.rowBands.length, m.colBands.length, m.cells, m.mode);
    cells[r][c] = value;
    this.matrix.set({ ...m, cells });
  }
  protected cellAt(r: number, c: number): string {
    return this.matrix().cells[r]?.[c] ?? (this.matrix().mode === 'Decision' ? 'ManualReview' : '0');
  }

  protected toNumberOrNull(v: string): number | null {
    return v === '' ? null : Number(v);
  }

  protected save(): void {
    this.ref.close({ label: this.label, config: this.buildConfig() });
  }

  /** Monta o JSON de config do nó, incluindo as ações anexadas quando houver. */
  private buildConfig(): string {
    // Só inclui listas de ações não-vazias, para não poluir o JSON.
    const opt = (a: ActionRow[]) => (a.length > 0 ? a : undefined);
    switch (this.data.kind) {
      case 'Condition':
        return JSON.stringify({
          expression: this.expression,
          trueActions: opt(this.trueActions()),
          falseActions: opt(this.falseActions()),
        });
      case 'Computation':
        return JSON.stringify({ assignments: this.assignments(), actions: opt(this.nodeActions()) });
      case 'Decision':
        return JSON.stringify({ outcome: this.outcome, message: this.message, actions: opt(this.nodeActions()) });
      case 'DataSource':
        return JSON.stringify({ source: this.source, actions: opt(this.nodeActions()) });
      case 'Comment':
        this.label = this.text; // o rótulo do comentário é o próprio texto
        return JSON.stringify({ text: this.text });
      case 'Action':
        return JSON.stringify({ actions: this.actions() });
      case 'Matrix':
        return JSON.stringify(this.matrix());
      default:
        return normalizeJson(this.rawJson);
    }
  }

  protected delete(): void {
    this.ref.close({ label: this.label, config: this.data.config, deleted: true });
  }

  /** Fecha salvando a config atual (com as ações) e pede para configurar a saída V/F. */
  protected configureBranch(branch: 'true' | 'false'): void {
    this.ref.close({ label: this.label, config: this.buildConfig(), configureBranch: branch });
  }
}

function safeParse(json: string): unknown {
  try {
    return JSON.parse(json || '{}');
  } catch {
    return {};
  }
}

function normalizeJson(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json));
  } catch {
    return json || '{}';
  }
}

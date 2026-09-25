import { Component, Inject, signal } from '@angular/core';
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
} from '../api/models';

export interface NodeConfigData {
  kind: FlowNodeKind;
  label: string;
  config: string;
  readOnly: boolean;
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
  "Campos 'campo', variáveis {variavel}, texto \"texto\", fontes [Fonte;Produto;Dado], funções (SE, ARRED…). Separador: ;";

/**
 * Diálogo de configuração de um nó, por tipo. Cobre Condition, Computation,
 * Decision, DataSource, Comment e Action com editores dedicados; para tipos com
 * config mais rica (Matrix, Ruleset) oferece um editor de JSON cru como fallback,
 * garantindo que nenhuma configuração fique inacessível.
 */
@Component({
  selector: 'app-node-config-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
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
  protected actions = signal<{ type: string; name?: string | null; expression: string }[]>([]); // Action
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
      case 'Condition':
        this.expression = (cfg as { expression?: string }).expression ?? '';
        break;
      case 'Computation':
        this.assignments.set((cfg as ComputationConfig).assignments ?? []);
        break;
      case 'Decision':
        this.outcome = (cfg as DecisionConfig).outcome ?? 'Approved';
        this.message = (cfg as DecisionConfig).message ?? '';
        break;
      case 'DataSource':
        this.source = (cfg as { source?: string }).source ?? '';
        break;
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

  // --- Action helpers ---
  protected addAction(): void {
    this.actions.update((a) => [...a, { type: 'AddPoints', expression: '' }]);
  }
  protected removeAction(i: number): void {
    this.actions.update((a) => a.filter((_, j) => j !== i));
  }
  protected setAction(i: number, patch: Partial<{ type: string; name?: string | null; expression: string }>): void {
    this.actions.update((a) => a.map((x, j) => (j === i ? { ...x, ...patch } : x)));
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
    let config: string;
    switch (this.data.kind) {
      case 'Condition':
        config = JSON.stringify({ expression: this.expression });
        break;
      case 'Computation':
        config = JSON.stringify({ assignments: this.assignments() });
        break;
      case 'Decision':
        config = JSON.stringify({ outcome: this.outcome, message: this.message });
        break;
      case 'DataSource':
        config = JSON.stringify({ source: this.source });
        break;
      case 'Comment':
        config = JSON.stringify({ text: this.text });
        this.label = this.text; // o rótulo do comentário é o próprio texto
        break;
      case 'Action':
        config = JSON.stringify({ actions: this.actions() });
        break;
      case 'Matrix':
        config = JSON.stringify(this.matrix());
        break;
      default:
        config = normalizeJson(this.rawJson);
    }
    this.ref.close({ label: this.label, config });
  }

  protected delete(): void {
    this.ref.close({ label: this.label, config: this.data.config, deleted: true });
  }

  /** Fecha salvando a config atual e pede ao editor para configurar a saída V/F. */
  protected configureBranch(branch: 'true' | 'false'): void {
    this.ref.close({ label: this.label, config: JSON.stringify({ expression: this.expression }), configureBranch: branch });
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

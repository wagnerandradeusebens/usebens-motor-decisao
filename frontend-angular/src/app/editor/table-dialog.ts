import { Component, Inject, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkDrag, CdkDragHandle } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import type { GraphTable, GraphTableColumn, ParameterColumnType } from '../api/models';

/** Escopo da tabela, escolhido na criação. */
export type TableScope = 'local' | 'global';

export interface TableDialogData {
  mode: 'create' | 'edit';
  /** Quando true, permite escolher o escopo (local/global). No editor de política. */
  allowScope: boolean;
  scope: TableScope;
  table: GraphTable;
  readOnly: boolean;
}

export interface TableResult {
  scope: TableScope;
  table: GraphTable;
  deleted?: boolean;
}

const TYPES: { value: ParameterColumnType; label: string }[] = [
  { value: 'Number', label: 'número' },
  { value: 'Text', label: 'texto' },
  { value: 'Boolean', label: 'booleano' },
  { value: 'Date', label: 'data' },
];

/**
 * Modal para criar/editar uma tabela de parâmetros (lookup). Define nome, rótulo,
 * escopo (local/global), colunas (nome + tipo), qual coluna é a chave (busca
 * exata) e/ou min/max (busca por faixa), o valor padrão, e a grade de linhas.
 * O container é redimensionável (canto inferior direito) para tabelas maiores.
 */
@Component({
  selector: 'app-table-dialog',
  imports: [
    FormsModule,
    CdkDrag,
    CdkDragHandle,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <div class="resizable" cdkDrag cdkDragRootElement=".cdk-overlay-pane">
      <h2 mat-dialog-title cdkDragHandle class="drag-title">
        {{ data.mode === 'create' ? 'Nova tabela' : 'Editar tabela' }}
        <mat-icon class="drag-grip" title="Arraste para mover">drag_indicator</mat-icon>
      </h2>
      <mat-dialog-content>
        @if (confirmingDelete()) {
          <div class="confirm">
            <mat-icon>warning</mat-icon>
            <div><strong>Excluir a tabela "{{ name() || '(sem nome)' }}"?</strong></div>
          </div>
        }

        <!-- Identificação -->
        <div class="row">
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Nome</mat-label>
            <input matInput [ngModel]="name()" (ngModelChange)="name.set($event)" [disabled]="readOnly"
                   placeholder="ex.: taxa_uf" autocomplete="off" />
            <mat-hint>Usada nas fórmulas: <code>PROCV("{{ name() || 'nome' }}"; "coluna"; chave)</code></mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Rótulo</mat-label>
            <input matInput [ngModel]="label()" (ngModelChange)="label.set($event)" [disabled]="readOnly"
                   placeholder="ex.: Taxa por UF" autocomplete="off" />
          </mat-form-field>
          @if (data.allowScope) {
            <mat-form-field appearance="outline" class="scope">
              <mat-label>Escopo</mat-label>
              <mat-select [ngModel]="scope()" (ngModelChange)="scope.set($event)"
                          [disabled]="readOnly || data.mode === 'edit'">
                <mat-option value="local">Local (esta política)</mat-option>
                <mat-option value="global">Global (todas)</mat-option>
              </mat-select>
            </mat-form-field>
          }
        </div>

        <!-- Colunas -->
        <div class="section-label">Colunas</div>
        <div class="cols">
          @for (c of columns(); track $index; let i = $index) {
            <div class="col-row">
              <mat-form-field appearance="outline" class="grow">
                <mat-label>nome</mat-label>
                <input matInput [ngModel]="c.name" (ngModelChange)="setColumn(i, { name: $event })"
                       [disabled]="readOnly" placeholder="ex.: uf" />
              </mat-form-field>
              <mat-form-field appearance="outline" class="type">
                <mat-label>tipo</mat-label>
                <mat-select [ngModel]="c.type" (ngModelChange)="setColumn(i, { type: $event })" [disabled]="readOnly">
                  @for (t of types; track t.value) {
                    <mat-option [value]="t.value">{{ t.label }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              @if (!readOnly) {
                <button matIconButton (click)="removeColumn(i)" title="Remover coluna"><mat-icon>close</mat-icon></button>
              }
            </div>
          }
          @if (!readOnly) {
            <button matButton="outlined" (click)="addColumn()"><mat-icon>add</mat-icon> Coluna</button>
          }
        </div>

        <!-- Como consultar -->
        <div class="section-label">Como consultar</div>
        <p class="help">
          Defina a <strong>coluna-chave</strong> para busca exata (<code>PROCV</code>) e/ou as colunas de
          <strong>mínimo/máximo</strong> para busca por faixa (<code>PROCV.FAIXA</code>, mín ≤ valor &lt; máx).
        </p>
        <div class="row">
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Coluna-chave (PROCV)</mat-label>
            <mat-select [ngModel]="keyColumn()" (ngModelChange)="keyColumn.set($event)" [disabled]="readOnly">
              <mat-option [value]="null">— nenhuma —</mat-option>
              @for (c of columnNames(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Coluna mínimo (faixa)</mat-label>
            <mat-select [ngModel]="minColumn()" (ngModelChange)="minColumn.set($event)" [disabled]="readOnly">
              <mat-option [value]="null">— nenhuma —</mat-option>
              @for (c of columnNames(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Coluna máximo (faixa)</mat-label>
            <mat-select [ngModel]="maxColumn()" (ngModelChange)="maxColumn.set($event)" [disabled]="readOnly">
              <mat-option [value]="null">— nenhuma —</mat-option>
              @for (c of columnNames(); track c) { <mat-option [value]="c">{{ c }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="grow">
            <mat-label>Valor padrão</mat-label>
            <input matInput [ngModel]="defaultValue()" (ngModelChange)="defaultValue.set($event)"
                   [disabled]="readOnly" placeholder="quando não casa" />
          </mat-form-field>
        </div>

        <!-- Linhas -->
        <div class="section-label rows-head">
          <span>Linhas</span>
          @if (!readOnly && columns().length > 0) {
            <span class="rows-tools">
              <span class="rows-hint">Cole (Ctrl+V) do Excel ou</span>
              <button matButton (click)="fileInput.click()">
                <mat-icon>upload_file</mat-icon> Importar CSV
              </button>
              <input #fileInput type="file" accept=".csv,text/csv" hidden (change)="onFileSelected($event)" />
            </span>
          }
        </div>
        @if (columns().length === 0) {
          <p class="help">Adicione colunas para montar a tabela.</p>
        } @else {
          <div class="grid" (paste)="onPaste($event)">
            <table>
              <thead>
                <tr>
                  <th class="rownum"></th>
                  @for (c of columns(); track $index) { <th>{{ c.name || 'col ' + ($index + 1) }}</th> }
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (r of rows(); track $index; let ri = $index) {
                  <tr>
                    <th class="rownum">{{ ri + 1 }}</th>
                    @for (c of columns(); track $index; let ci = $index) {
                      <td>
                        <input class="cell" [ngModel]="cellAt(ri, ci)" (ngModelChange)="setCell(ri, ci, $event)"
                               [disabled]="readOnly" />
                      </td>
                    }
                    <td>
                      @if (!readOnly) {
                        <button matIconButton (click)="removeRow(ri)" title="Remover linha"><mat-icon>close</mat-icon></button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (!readOnly) {
            <button matButton="outlined" (click)="addRow()"><mat-icon>add</mat-icon> Linha</button>
          }
        }
      </mat-dialog-content>
      <mat-dialog-actions>
        @if (data.mode === 'edit' && !readOnly) {
          @if (!confirmingDelete()) {
            <button matButton class="danger" (click)="confirmingDelete.set(true)"><mat-icon>delete</mat-icon> Excluir</button>
          } @else {
            <button matButton class="danger" (click)="delete()"><mat-icon>delete_forever</mat-icon> Confirmar</button>
            <button matButton (click)="confirmingDelete.set(false)">Cancelar exclusão</button>
          }
        }
        <span class="spacer"></span>
        <button matButton mat-dialog-close>Cancelar</button>
        <button matButton="filled" (click)="confirm()" [disabled]="readOnly || !name().trim() || confirmingDelete()">
          {{ data.mode === 'create' ? 'Criar' : 'Salvar' }}
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: `
    .resizable {
      display: flex; flex-direction: column; resize: both; overflow: auto;
      min-width: 560px; min-height: 380px; max-width: 94vw; max-height: 92vh; width: 860px;
    }
    mat-dialog-content { flex: 1 1 auto; padding-top: 1.25rem; }
    .drag-title { display: flex; align-items: center; justify-content: space-between; cursor: move; user-select: none; margin-bottom: 0; }
    .drag-grip { color: var(--cor-texto-claro); }
    /* O mat-hint do campo Nome ocupa a área de subscript; a linha precisa de uma
       margem inferior para o cabeçalho "Colunas" não colar/sobrepor o hint. */
    .row { display: flex; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 0.5rem; }
    .grow { flex: 1; min-width: 160px; }
    .scope { width: 200px; }
    .type { width: 140px; }
    .section-label { font-weight: 600; font-size: 0.9rem; color: var(--marca-azul); margin: 1.25rem 0 0.5rem; }
    .section-label:first-of-type { margin-top: 0.5rem; }
    .rows-head { display: flex; align-items: center; justify-content: space-between; }
    .rows-tools { display: flex; align-items: center; gap: 0.4rem; }
    .rows-hint { font-weight: 400; font-size: 0.75rem; color: var(--cor-texto-claro); }
    .help { font-size: 0.78rem; color: var(--cor-texto-claro); margin: 0 0 0.5rem; }
    .cols .col-row { display: flex; align-items: center; gap: 0.5rem; }
    .grid { overflow: auto; border: 1px solid var(--cor-borda); border-radius: 6px; margin-bottom: 0.5rem; max-height: 40vh; }
    .grid table { border-collapse: collapse; width: 100%; font-size: 0.8rem; }
    .grid th, .grid td { border: 1px solid var(--cor-borda); padding: 0; }
    .grid thead th { background: var(--cor-fundo); padding: 4px 8px; font-weight: 600; color: var(--marca-azul); white-space: nowrap; }
    .grid .rownum { background: var(--cor-fundo); color: var(--cor-texto-claro); text-align: center; width: 34px; padding: 4px; }
    .cell { width: 100%; box-sizing: border-box; border: none; padding: 5px 8px; font-size: 0.8rem; background: transparent; }
    .cell:focus { outline: 2px solid var(--marca-teal); outline-offset: -2px; }
    .spacer { flex: 1 1 auto; }
    .danger { color: var(--cor-perigo); }
    .confirm { display: flex; align-items: center; gap: 0.6rem; background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 0.6rem 0.85rem; border-radius: 8px; margin-bottom: 1rem; }
  `,
})
export class TableDialog {
  protected readonly types = TYPES;

  protected readonly name = signal('');
  protected readonly label = signal('');
  protected readonly scope = signal<TableScope>('local');
  protected readonly columns = signal<GraphTableColumn[]>([]);
  protected readonly rows = signal<string[][]>([]);
  protected readonly keyColumn = signal<string | null>(null);
  protected readonly minColumn = signal<string | null>(null);
  protected readonly maxColumn = signal<string | null>(null);
  protected readonly defaultValue = signal<string | null>(null);
  protected readonly confirmingDelete = signal(false);

  protected readonly columnNames = computed(() => this.columns().map((c) => c.name).filter((n) => !!n));

  protected get readOnly(): boolean {
    return this.data.readOnly;
  }

  constructor(
    private readonly ref: MatDialogRef<TableDialog, TableResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: TableDialogData,
  ) {
    const t = data.table;
    this.name.set(t.name);
    this.label.set(t.label);
    this.scope.set(data.scope);
    this.columns.set(t.columns.map((c) => ({ ...c })));
    this.rows.set(t.rows.map((r) => [...r]));
    this.keyColumn.set(t.keyColumn);
    this.minColumn.set(t.minColumn);
    this.maxColumn.set(t.maxColumn);
    this.defaultValue.set(t.defaultValue);
  }

  // --- Colunas ---
  protected addColumn(): void {
    this.columns.update((cs) => [...cs, { name: '', type: 'Text' }]);
    // Ajusta as linhas para o novo número de colunas.
    this.rows.update((rs) => rs.map((r) => [...r, '']));
  }
  protected removeColumn(i: number): void {
    this.columns.update((cs) => cs.filter((_, j) => j !== i));
    this.rows.update((rs) => rs.map((r) => r.filter((_, j) => j !== i)));
  }
  protected setColumn(i: number, patch: Partial<GraphTableColumn>): void {
    this.columns.update((cs) => cs.map((c, j) => (j === i ? { ...c, ...patch } : c)));
  }

  // --- Linhas ---
  protected addRow(): void {
    this.rows.update((rs) => [...rs, Array(this.columns().length).fill('')]);
  }
  protected removeRow(i: number): void {
    this.rows.update((rs) => rs.filter((_, j) => j !== i));
  }
  protected cellAt(r: number, c: number): string {
    return this.rows()[r]?.[c] ?? '';
  }
  protected setCell(r: number, c: number, value: string): void {
    this.rows.update((rs) =>
      rs.map((row, ri) => (ri === r ? row.map((v, ci) => (ci === c ? value : v)) : row)),
    );
  }

  /**
   * Colar do Excel/planilha: o clipboard vem como TSV (colunas por Tab, linhas por
   * quebra). Substitui as linhas pelo conteúdo colado, ajustando à contagem de
   * colunas atual. Só cola quando há colunas definidas.
   */
  protected onPaste(event: ClipboardEvent): void {
    if (this.readOnly || this.columns().length === 0) return;
    const text = event.clipboardData?.getData('text/plain');
    if (!text) return;
    event.preventDefault();
    // Do Excel/planilha o clipboard vem como TSV (colunas por Tab).
    this.applyImportedText(text, '\t');
  }

  /** Handler do <input type=file>: lê o CSV em memória e preenche a grade. */
  protected onFileSelected(event: Event): void {
    if (this.readOnly || this.columns().length === 0) return;
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result ?? '');
      // Detecta o separador do CSV: ';' (padrão BR do Excel) ou ','.
      const sep = this.detectCsvSeparator(text);
      this.applyImportedText(text, sep);
    };
    reader.readAsText(file, 'utf-8');
    // Permite reimportar o mesmo arquivo (reseta o input).
    input.value = '';
  }

  /** Heurística simples: se a 1ª linha tem mais ';' que ',', usa ';'. */
  private detectCsvSeparator(text: string): string {
    const firstLine = text.replace(/\r/g, '').split('\n')[0] ?? '';
    const commas = (firstLine.match(/,/g) ?? []).length;
    const semis = (firstLine.match(/;/g) ?? []).length;
    return semis >= commas ? ';' : ',';
  }

  /**
   * Preenche a grade a partir de um texto delimitado (TSV do paste ou CSV do
   * arquivo). SUBSTITUI as linhas atuais. Normaliza cada célula à contagem de
   * colunas e aplica a normalização decimal BR nas colunas do tipo Number.
   * O arquivo/clipboard é apenas lido — nada é armazenado além dos dados.
   */
  private applyImportedText(text: string, sep: string): void {
    const cols = this.columns();
    const lines = text.replace(/\r/g, '').split('\n').filter((l) => l.trim().length > 0);
    if (lines.length === 0) return;

    const parsed = lines.map((line) => {
      const cells = this.splitDelimited(line, sep);
      return cols.map((col, i) => this.normalizeCell((cells[i] ?? '').trim(), col.type));
    });
    this.rows.set(parsed);
  }

  /** Divide uma linha por `sep`, respeitando aspas duplas (campos com o separador). */
  private splitDelimited(line: string, sep: string): string[] {
    const out: string[] = [];
    let cur = '';
    let inQuotes = false;
    for (let i = 0; i < line.length; i++) {
      const ch = line[i];
      if (ch === '"') {
        if (inQuotes && line[i + 1] === '"') { cur += '"'; i++; }
        else inQuotes = !inQuotes;
      } else if (ch === sep && !inQuotes) {
        out.push(cur);
        cur = '';
      } else {
        cur += ch;
      }
    }
    out.push(cur);
    return out;
  }

  /**
   * Normaliza uma célula conforme o tipo da coluna. Para Number, converte do
   * formato BR (milhar '.', decimal ',') para o formato do motor (decimal '.'):
   * remove separador de milhar e troca a vírgula decimal por ponto.
   */
  private normalizeCell(raw: string, type: ParameterColumnType): string {
    if (type !== 'Number' || raw.length === 0) return raw;
    // Só normaliza quando parece um número BR (dígitos, '.', ',', sinal).
    if (!/^[-+]?[\d.,]+$/.test(raw)) return raw;
    // Ex.: "1.234.567,89" -> "1234567.89"; "1234,5" -> "1234.5".
    return raw.replace(/\./g, '').replace(',', '.');
  }

  protected confirm(): void {
    const name = this.name().trim();
    if (!name) return;
    const table: GraphTable = {
      name,
      label: this.label().trim() || name,
      columns: this.columns().filter((c) => c.name.trim().length > 0),
      rows: this.rows(),
      keyColumn: this.keyColumn(),
      minColumn: this.minColumn(),
      maxColumn: this.maxColumn(),
      defaultValue: this.defaultValue() && this.defaultValue()!.length > 0 ? this.defaultValue() : null,
    };
    this.ref.close({ scope: this.scope(), table });
  }

  protected delete(): void {
    this.ref.close({ scope: this.data.scope, table: this.data.table, deleted: true });
  }
}

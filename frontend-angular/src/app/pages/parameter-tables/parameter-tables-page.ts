import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ApiService } from '../../api/api.service';
import type { GlobalParameterTable, GraphTable } from '../../api/models';
import { apiErrorMessage } from '../../shared/format';
import { TableDialog, TableDialogData, TableResult } from '../../editor/table-dialog';

/**
 * CRUD de tabelas de parâmetros globais (compartilhadas entre políticas). Reusa o
 * TableDialog do editor (aqui sempre no escopo global). Uma tabela local de mesmo
 * nome tem prioridade na política que a define.
 */
@Component({
  selector: 'app-parameter-tables-page',
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule, MatDialogModule],
  templateUrl: './parameter-tables-page.html',
  styleUrl: './parameter-tables-page.scss',
})
export class ParameterTablesPage {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);

  protected readonly items = signal<GlobalParameterTable[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.listGlobalTables().subscribe({
      next: (list) => {
        this.items.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar tabelas globais.'));
        this.loading.set(false);
      },
    });
  }

  /** Resumo das colunas para o card (nomes separados por vírgula). */
  protected columnsSummary(t: GlobalParameterTable): string {
    return t.columns.map((c) => c.name).join(', ') || '(sem colunas)';
  }

  protected openNew(): void {
    this.openDialog(null);
  }

  protected openEdit(t: GlobalParameterTable): void {
    this.openDialog(t);
  }

  private toGraphTable(t: GlobalParameterTable | null): GraphTable {
    return t
      ? {
          name: t.name, label: t.label, columns: t.columns, rows: t.rows,
          keyColumn: t.keyColumn, minColumn: t.minColumn, maxColumn: t.maxColumn, defaultValue: t.defaultValue,
        }
      : { name: '', label: '', columns: [], rows: [], keyColumn: null, minColumn: null, maxColumn: null, defaultValue: null };
  }

  private openDialog(existing: GlobalParameterTable | null): void {
    const data: TableDialogData = {
      mode: existing ? 'edit' : 'create',
      allowScope: false, // esta tela é sempre global
      scope: 'global',
      table: this.toGraphTable(existing),
      readOnly: false,
    };
    const ref = this.dialog.open<TableDialog, TableDialogData, TableResult>(TableDialog, {
      data,
      maxWidth: '94vw',
      panelClass: 'resizable-dialog',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      this.error.set(null);

      if (result.deleted && existing) {
        this.api.deleteGlobalTable(existing.id).subscribe({
          next: () => this.load(),
          error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao excluir a tabela global.')),
        });
        return;
      }

      const input = {
        name: result.table.name,
        label: result.table.label,
        columns: result.table.columns,
        rows: result.table.rows,
        keyColumn: result.table.keyColumn,
        minColumn: result.table.minColumn,
        maxColumn: result.table.maxColumn,
        defaultValue: result.table.defaultValue,
      };
      const req = existing
        ? this.api.updateGlobalTable(existing.id, input)
        : this.api.createGlobalTable(input);
      req.subscribe({
        next: () => this.load(),
        error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao salvar a tabela global.')),
      });
    });
  }
}

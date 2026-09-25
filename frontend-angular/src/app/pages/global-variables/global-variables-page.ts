import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ApiService } from '../../api/api.service';
import type { GlobalVariable } from '../../api/models';
import { apiErrorMessage } from '../../shared/format';
import { GlobalVariableDialog, GlobalVariableResult } from './global-variable-dialog';

/**
 * CRUD de variáveis globais (reutilizáveis entre políticas). Uma variável local
 * de mesmo nome tem prioridade na política que a define.
 */
@Component({
  selector: 'app-global-variables-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatDialogModule,
  ],
  templateUrl: './global-variables-page.html',
  styleUrl: './global-variables-page.scss',
})
export class GlobalVariablesPage {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);

  protected readonly items = signal<GlobalVariable[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.listGlobalVariables().subscribe({
      next: (list) => {
        this.items.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar variáveis globais.'));
        this.loading.set(false);
      },
    });
  }

  protected openNew(): void {
    this.openDialog(null);
  }

  protected openEdit(v: GlobalVariable): void {
    this.openDialog(v);
  }

  private openDialog(existing: GlobalVariable | null): void {
    const ref = this.dialog.open<GlobalVariableDialog, GlobalVariable | null, GlobalVariableResult>(
      GlobalVariableDialog,
      { width: '560px', data: existing },
    );
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      this.error.set(null);
      const req = existing
        ? this.api.updateGlobalVariable(existing.id, result.key, result.key, result.expression)
        : this.api.createGlobalVariable(result.key, result.key, result.expression);
      req.subscribe({
        next: () => this.load(),
        error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao salvar a variável global.')),
      });
    });
  }

  protected remove(v: GlobalVariable): void {
    if (!window.confirm(`Excluir a variável global "${v.key}"?`)) return;
    this.error.set(null);
    this.api.deleteGlobalVariable(v.id).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao excluir a variável global.')),
    });
  }
}

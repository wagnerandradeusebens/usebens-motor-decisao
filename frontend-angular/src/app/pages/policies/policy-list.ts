import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../api/api.service';
import type { FlowSummary } from '../../api/models';
import { apiErrorMessage, fmtDate } from '../../shared/format';
import { NewPolicyDialog, NewPolicyResult } from './new-policy-dialog';

/**
 * Página Políticas: área superior com a lista de políticas (indicador de
 * publicada) e área inferior com o histórico de versões da política selecionada
 * (status + datas). "Nova política" abre um diálogo. Espelha o FlowListPage React.
 */
@Component({
  selector: 'app-policy-list',
  imports: [
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatTableModule,
    MatDialogModule,
    MatProgressBarModule,
  ],
  templateUrl: './policy-list.html',
  styleUrl: './policy-list.scss',
})
export class PolicyList {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected readonly flows = signal<FlowSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selectedId = signal<string | null>(null);

  protected readonly selected = computed(
    () => this.flows().find((f) => f.id === this.selectedId()) ?? null,
  );

  protected readonly historyColumns = ['version', 'status', 'created', 'updated', 'published', 'open'];

  protected readonly fmt = fmtDate;

  constructor() {
    this.load();
  }

  protected isPublished(f: FlowSummary): boolean {
    return f.versions.some((v) => v.status === 'Published');
  }

  /** Versões ordenadas da mais nova para a mais antiga (para a tabela de histórico). */
  protected sortedVersions(f: FlowSummary) {
    return [...f.versions].sort((a, b) => b.versionNumber - a.versionNumber);
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  private load(): void {
    this.loading.set(true);
    this.api.listFlows().subscribe({
      next: (list) => {
        this.flows.set(list);
        const cur = this.selectedId();
        this.selectedId.set(cur && list.some((f) => f.id === cur) ? cur : (list[0]?.id ?? null));
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar políticas.'));
        this.loading.set(false);
      },
    });
  }

  protected openCreate(): void {
    const ref = this.dialog.open<NewPolicyDialog, unknown, NewPolicyResult>(NewPolicyDialog, {
      width: '520px',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      this.error.set(null);
      this.api.createFlow(result.name, result.description).subscribe({
        next: (flow) => {
          // Abre direto a edição da 1ª versão da nova política (rota do editor).
          const v = flow.versions[0];
          if (v) {
            this.router.navigate(['/politicas', flow.id, 'versions', v.id]);
          } else {
            this.load();
          }
        },
        error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao criar política.')),
      });
    });
  }

  protected remove(id: string, name: string, event: MouseEvent): void {
    event.stopPropagation();
    const ok = window.confirm(
      `Excluir a política "${name}"? Esta ação é irreversível e remove todas as versões e execuções.`,
    );
    if (!ok) return;
    this.error.set(null);
    this.api.deleteFlow(id).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao excluir a política.')),
    });
  }
}

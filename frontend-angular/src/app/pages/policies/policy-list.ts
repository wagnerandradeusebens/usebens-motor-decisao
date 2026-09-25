import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../api/api.service';
import type { FlowSummary, FlowVersionSummary } from '../../api/models';
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

  /**
   * Detalhe da política selecionada, carregado via GET /flows/{id}. É a fonte do
   * histórico porque, diferente do GET /flows (listagem), o detalhe traz o
   * "Vinculado a" (linkedPolicies) por versão. Cai de volta ao item da lista
   * enquanto o detalhe não chegou.
   */
  protected readonly selectedDetail = signal<FlowSummary | null>(null);

  protected readonly selected = computed(() => {
    const detail = this.selectedDetail();
    if (detail && detail.id === this.selectedId()) return detail;
    return this.flows().find((f) => f.id === this.selectedId()) ?? null;
  });

  protected readonly historyColumns = ['version', 'status', 'created', 'updated', 'published', 'linked', 'open'];

  /** Trava os botões enquanto uma nova versão está sendo criada. */
  protected readonly creating = signal(false);

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
    this.loadDetail(id);
  }

  /** Carrega o detalhe (com linkedPolicies) da política selecionada. */
  private loadDetail(id: string): void {
    this.api.getFlow(id).subscribe({
      next: (f) => {
        // Só aplica se ainda for a política selecionada (evita corrida de cliques).
        if (this.selectedId() === id) this.selectedDetail.set(f);
      },
      error: () => {
        // Silencioso: o histórico cai no item da listagem (sem "Vinculado a").
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.listFlows().subscribe({
      next: (list) => {
        this.flows.set(list);
        const cur = this.selectedId();
        const next = cur && list.some((f) => f.id === cur) ? cur : (list[0]?.id ?? null);
        this.selectedId.set(next);
        if (next) this.loadDetail(next);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar políticas.'));
        this.loading.set(false);
      },
    });
  }

  /**
   * Uma versão pode ser excluída quando não está publicada, não está vinculada
   * (congelada em outra política) e não é a única versão da política. O backend
   * reforça as mesmas regras — aqui é só para habilitar/desabilitar o botão.
   */
  protected canDeleteVersion(v: FlowVersionSummary): boolean {
    const f = this.selected();
    if (!f || f.versions.length <= 1) return false;
    return v.status !== 'Published' && !(v.linkedPolicies?.length);
  }

  /** Motivo (tooltip) de o botão de excluir estar desabilitado. */
  protected deleteVersionHint(v: FlowVersionSummary): string {
    const f = this.selected();
    if (f && f.versions.length <= 1) return 'Única versão — exclua a política inteira';
    if (v.status === 'Published') return 'Versão publicada não pode ser excluída';
    if (v.linkedPolicies?.length) return 'Versão vinculada a outra política não pode ser excluída';
    return 'Excluir esta versão';
  }

  /** Exclui a versão após confirmação. Recarrega o histórico ao concluir. */
  protected deleteVersion(v: FlowVersionSummary, event: MouseEvent): void {
    event.stopPropagation();
    const flowId = this.selectedId();
    if (!flowId || !this.canDeleteVersion(v)) return;
    const ok = window.confirm(
      `Excluir a versão v${v.versionNumber}? Esta ação é irreversível e remove o grafo e as execuções desta versão.`,
    );
    if (!ok) return;
    this.error.set(null);
    this.api.deleteVersion(flowId, v.id).subscribe({
      next: () => {
        // Recarrega listagem (contagem de versões) e o detalhe do histórico.
        this.load();
        this.loadDetail(flowId);
      },
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao excluir a versão.')),
    });
  }

  /**
   * Cria uma nova versão (rascunho) copiando a versão selecionada e abre o editor
   * dela. O backend permite criar nova versão mesmo a partir de uma versão
   * congelada/publicada — o congelamento só bloqueia editar a versão existente.
   */
  protected newVersionFrom(versionId: string): void {
    const flowId = this.selectedId();
    if (!flowId || this.creating()) return;
    this.creating.set(true);
    this.error.set(null);
    this.api.createVersion(flowId, versionId).subscribe({
      next: (v) => {
        this.creating.set(false);
        this.router.navigate(['/politicas', flowId, 'versions', v.id]);
      },
      error: (e) => {
        this.creating.set(false);
        this.error.set(apiErrorMessage(e, 'Falha ao criar versão.'));
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

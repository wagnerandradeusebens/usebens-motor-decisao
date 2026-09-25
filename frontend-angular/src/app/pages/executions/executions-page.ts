import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../api/api.service';
import type { ExecutionDetail, ExecutionSummary } from '../../api/models';
import { apiErrorMessage, fmtDate } from '../../shared/format';
import { download, groupByCategory, pretty, toTextLog, TraceGroup } from '../../shared/trace';

/**
 * Log detalhado de execuções de uma política: lista de execuções recentes e, para
 * a selecionada, a trilha passo a passo agrupada por blocos, com a árvore de
 * resolução das fórmulas expansível, além de downloads (txt/json).
 */
@Component({
  selector: 'app-executions-page',
  imports: [
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatExpansionModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './executions-page.html',
  styleUrl: './executions-page.scss',
})
export class ExecutionsPage {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly flowId = this.route.snapshot.paramMap.get('flowId')!;
  protected readonly items = signal<ExecutionSummary[]>([]);
  protected readonly detail = signal<ExecutionDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly groups = computed<TraceGroup[]>(() => {
    const d = this.detail();
    return d ? groupByCategory(d.trace) : [];
  });

  protected readonly fmt = fmtDate;
  protected readonly pretty = pretty;

  constructor() {
    this.api.listExecutions(this.flowId).subscribe({
      next: (list) => {
        this.items.set(list);
        this.loading.set(false);
        if (list[0]) this.open(list[0].id);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar execuções.'));
        this.loading.set(false);
      },
    });
  }

  protected open(id: string): void {
    this.api.getExecution(id).subscribe({
      next: (d) => this.detail.set(d),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao carregar a execução.')),
    });
  }

  protected outputEntries(d: ExecutionDetail): { key: string; value: string }[] {
    return Object.entries(d.outputs).map(([key, value]) => ({ key, value }));
  }

  protected downloadTxt(): void {
    const d = this.detail();
    if (!d) return;
    download(`execucao-${d.id}.txt`, toTextLog(d), 'text/plain;charset=utf-8');
  }

  protected downloadJson(): void {
    const d = this.detail();
    if (!d) return;
    download(`execucao-${d.id}.json`, JSON.stringify(d, null, 2), 'application/json');
  }
}

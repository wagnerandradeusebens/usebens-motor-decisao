import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../api/api.service';
import type { FlowSummary } from '../../api/models';
import { apiErrorMessage, fmtDate } from '../../shared/format';

/** Detalhe de uma política: lista de versões e ações (nova/duplicar/abrir). */
@Component({
  selector: 'app-policy-detail',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatChipsModule, MatIconModule],
  templateUrl: './policy-detail.html',
  styleUrl: './policy-detail.scss',
})
export class PolicyDetail {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  protected readonly flow = signal<FlowSummary | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly flowId = this.route.snapshot.paramMap.get('flowId')!;

  protected readonly fmt = fmtDate;

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.getFlow(this.flowId).subscribe({
      next: (f) => this.flow.set(f),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao carregar a política.')),
    });
  }

  protected newVersion(copyFrom?: string): void {
    this.error.set(null);
    this.api.createVersion(this.flowId, copyFrom).subscribe({
      next: () => this.load(),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao criar versão.')),
    });
  }
}

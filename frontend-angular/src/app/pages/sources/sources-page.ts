import { Component, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../api/api.service';
import type { SourceDescriptorDto } from '../../api/models';
import { apiErrorMessage } from '../../shared/format';

/**
 * Lista read-only das fontes externas registradas. Aparecem aqui conforme as
 * integrações são implementadas no backend; o usuário não as cria.
 */
@Component({
  selector: 'app-sources-page',
  imports: [MatCardModule, MatChipsModule, MatIconModule, MatProgressBarModule],
  templateUrl: './sources-page.html',
  styleUrl: './sources-page.scss',
})
export class SourcesPage {
  private readonly api = inject(ApiService);

  protected readonly sources = signal<SourceDescriptorDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.listSources().subscribe({
      next: (list) => {
        this.sources.set(list);
        this.loading.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar as fontes.'));
        this.loading.set(false);
      },
    });
  }
}

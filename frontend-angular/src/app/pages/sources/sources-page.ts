import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../../api/api.service';
import type { SourceConfigDto, SourceDescriptorDto } from '../../api/models';
import { apiErrorMessage } from '../../shared/format';

/**
 * Lista das fontes externas registradas e seus parâmetros operacionais
 * (tentativas, timeout, validade do cache), editáveis e salvos por fonte.
 */
@Component({
  selector: 'app-sources-page',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  templateUrl: './sources-page.html',
  styleUrl: './sources-page.scss',
})
export class SourcesPage {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);

  protected readonly sources = signal<SourceDescriptorDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Config editável por nome de fonte. */
  protected readonly configs = signal<Record<string, SourceConfigDto>>({});

  constructor() {
    this.api.listSources().subscribe({
      next: (list) => {
        this.sources.set(list);
        this.loading.set(false);
        // Carrega os parâmetros de cada fonte.
        for (const s of list) {
          this.api.getSourceConfig(s.name).subscribe({
            next: (cfg) => this.configs.update((c) => ({ ...c, [s.name]: cfg })),
            error: () => {},
          });
        }
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar as fontes.'));
        this.loading.set(false);
      },
    });
  }

  protected config(name: string): SourceConfigDto {
    return this.configs()[name] ?? { maxAttempts: 1, timeoutSeconds: 30, cacheTtlHours: 0 };
  }

  protected patchConfig(name: string, patch: Partial<SourceConfigDto>): void {
    this.configs.update((c) => ({ ...c, [name]: { ...this.config(name), ...patch } }));
  }

  protected saveConfig(name: string): void {
    this.api.updateSourceConfig(name, this.config(name)).subscribe({
      next: (cfg) => {
        this.configs.update((c) => ({ ...c, [name]: cfg }));
        this.snack.open(`Parâmetros de ${name} salvos.`, 'ok', { duration: 2500 });
      },
      error: (e) => this.snack.open(apiErrorMessage(e, 'Falha ao salvar parâmetros.'), 'ok', { duration: 3500 }),
    });
  }

  protected toNumber(v: string): number {
    return Number(v) || 0;
  }
}

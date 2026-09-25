import { Component, Inject, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../api/api.service';
import type { DecisionResponse, GraphInputField } from '../api/models';
import { apiErrorMessage } from '../shared/format';
import { groupByPolicy, PolicyTraceGroup } from '../shared/trace';

interface RunField {
  key: string;
  value: string;
  type: 'number' | 'text' | 'boolean' | 'date';
}

export interface DecisionRunnerData {
  flowId: string;
  inputFields: GraphInputField[];
  /** Versão a testar. Quando presente e `test` é true, roda em modo teste. */
  versionId?: string;
  /**
   * Modo teste: executa a versão informada (rascunho) sem publicar nem persistir.
   * Quando false/ausente, chama o endpoint normal (versão publicada).
   */
  test?: boolean;
}

function toValue(f: RunField): unknown {
  switch (f.type) {
    case 'number':
      return Number(f.value);
    case 'boolean':
      return f.value === 'true';
    default:
      return f.value;
  }
}

/** Modal "Testar decisão": monta os campos de entrada e chama /decisions. */
@Component({
  selector: 'app-decision-runner-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './decision-runner-dialog.html',
  styleUrl: './decision-runner-dialog.scss',
})
export class DecisionRunnerDialog {
  private readonly api = inject(ApiService);

  protected reference = 'TESTE-1';
  protected readonly fields = signal<RunField[]>([]);
  protected readonly result = signal<DecisionResponse | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly running = signal(false);

  protected readonly fieldTypes = [
    { value: 'number', label: 'número' },
    { value: 'text', label: 'texto' },
    { value: 'boolean', label: 'booleano' },
    { value: 'date', label: 'data' },
  ];

  constructor(@Inject(MAT_DIALOG_DATA) protected readonly data: DecisionRunnerData) {
    // Prefill a partir dos campos declarados da política; senão, um campo em branco.
    const declared = (data.inputFields ?? []).map<RunField>((f) => ({
      key: f.name,
      value: '',
      type: (f.type.toLowerCase() as RunField['type']) ?? 'text',
    }));
    this.fields.set(declared.length ? declared : [{ key: '', value: '', type: 'number' }]);
  }

  protected setField(i: number, patch: Partial<RunField>): void {
    this.fields.update((fs) => fs.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  }
  protected addField(): void {
    this.fields.update((fs) => [...fs, { key: '', value: '', type: 'number' }]);
  }
  protected removeField(i: number): void {
    this.fields.update((fs) => fs.filter((_, j) => j !== i));
  }

  protected outputEntries(r: DecisionResponse): { key: string; value: string }[] {
    return Object.entries(r.outputs).map(([key, value]) => ({ key, value }));
  }

  /** Trilha agrupada por política → categoria (para não misturar principal e subs). */
  protected policyGroups(r: DecisionResponse): PolicyTraceGroup[] {
    return groupByPolicy(r.trace);
  }

  protected run(): void {
    this.error.set(null);
    this.result.set(null);
    this.running.set(true);
    const payload: Record<string, unknown> = {};
    for (const f of this.fields()) {
      if (f.key.trim()) payload[f.key.trim()] = toValue(f);
    }
    // Modo teste (rascunho): executa a versão informada sem publicar/persistir.
    const call$ = this.data.test && this.data.versionId
      ? this.api.testDecide(this.data.flowId, this.data.versionId, this.reference || null, payload)
      : this.api.decide(this.data.flowId, this.reference || null, payload);
    call$.subscribe({
      next: (r) => {
        this.result.set(r);
        this.running.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao executar a decisão.'));
        this.running.set(false);
      },
    });
  }
}

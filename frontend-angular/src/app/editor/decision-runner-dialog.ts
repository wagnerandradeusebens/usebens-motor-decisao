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
import type { DecisionResponse, GraphInputField, PolicyInputSchema } from '../api/models';
import { apiErrorMessage } from '../shared/format';
import { groupByPolicy, PolicyTraceGroup } from '../shared/trace';

interface RunField {
  key: string;
  value: string;
  type: 'number' | 'text' | 'boolean' | 'date';
  /** Obrigatório (declarado ou exigido por fonte). Não pode faltar na requisição. */
  required?: boolean;
  /** Fontes que exigem o campo (tooltip). Vazio para campos manuais. */
  sources?: string[];
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

    // Busca o schema (campos declarados + obrigatórios derivados das fontes) para
    // marcar obrigatórios e garantir que os campos-chave apareçam no formulário.
    if (data.versionId) {
      this.api.getInputSchema(data.flowId, data.versionId).subscribe({
        next: (schema) => this.applySchema(schema),
      });
    }
  }

  /**
   * Mescla o schema no formulário: marca os obrigatórios (e suas fontes) e
   * adiciona os campos que faltam (ex.: campos-chave de fonte). Preserva valores
   * já digitados.
   */
  private applySchema(schema: PolicyInputSchema): void {
    this.fields.update((current) => {
      const byKey = new Map(current.map((f) => [f.key.trim().toLowerCase(), f]));
      for (const sf of schema.fields) {
        const existing = byKey.get(sf.name.toLowerCase());
        if (existing) {
          existing.required = sf.required || existing.required;
          existing.sources = sf.requiredBySources;
        } else {
          const nf: RunField = {
            key: sf.name,
            value: '',
            type: (sf.type.toLowerCase() as RunField['type']) ?? 'text',
            required: sf.required,
            sources: sf.requiredBySources,
          };
          byKey.set(sf.name.toLowerCase(), nf);
        }
      }
      // Mantém a ordem: primeiro os já existentes, depois os novos do schema.
      const seen = new Set<string>();
      const ordered: RunField[] = [];
      for (const f of current) {
        ordered.push(byKey.get(f.key.trim().toLowerCase()) ?? f);
        seen.add(f.key.trim().toLowerCase());
      }
      for (const sf of schema.fields) {
        const k = sf.name.toLowerCase();
        if (!seen.has(k)) {
          ordered.push(byKey.get(k)!);
          seen.add(k);
        }
      }
      return ordered;
    });
  }

  /** Tooltip de um campo: se obrigatório e de fonte, mostra as fontes. */
  protected fieldHint(f: RunField): string {
    if (!f.required) return '';
    if (f.sources && f.sources.length > 0) {
      return 'Obrigatório · exigido por: ' + f.sources.join(', ');
    }
    return 'Obrigatório';
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

    // Valida obrigatórios antes de enviar (mesma regra do backend): dá um
    // feedback imediato em vez de esperar o 400.
    const missing = this.fields()
      .filter((f) => f.required && !String(f.value ?? '').trim())
      .map((f) => f.key);
    if (missing.length > 0) {
      this.error.set('Preencha os campos obrigatórios: ' + missing.join(', ') + '.');
      return;
    }

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

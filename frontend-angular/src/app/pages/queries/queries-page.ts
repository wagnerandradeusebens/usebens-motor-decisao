import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../api/api.service';
import type { DecisionResponse, FlowSummary, InputSchemaField, PolicyInputSchema } from '../../api/models';
import { apiErrorMessage } from '../../shared/format';
import { groupByPolicy, PolicyTraceGroup, sourcesReport, SourceReportGroup } from '../../shared/trace';

/** Um campo do formulário de consulta (com valor e metadados do schema). */
interface QueryField {
  name: string;
  label: string;
  type: 'number' | 'text' | 'boolean' | 'date';
  required: boolean;
  group: string | null;
  value: string;
}

/** Grupo de campos por assunto, para renderizar o formulário em seções. */
interface QueryGroup {
  group: string | null;
  label: string;
  fields: QueryField[];
}

/**
 * Página de Consultas: o usuário escolhe uma política publicada (a "consulta"),
 * o formulário de campos abre (agrupado por assunto, com obrigatórios), e o botão
 * Consultar executa a decisão e mostra o resultado + a trilha. As consultas
 * disponíveis são as políticas com versão publicada.
 */
@Component({
  selector: 'app-queries-page',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './queries-page.html',
  styleUrl: './queries-page.scss',
})
export class QueriesPage {
  private readonly api = inject(ApiService);

  /** Políticas publicadas (consultas disponíveis). */
  protected readonly policies = signal<FlowSummary[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly loadingSchema = signal(false);
  protected readonly running = signal(false);
  protected readonly error = signal<string | null>(null);

  protected reference = '';
  protected readonly fields = signal<QueryField[]>([]);
  protected readonly result = signal<DecisionResponse | null>(null);

  /** Campos agrupados por assunto (na ordem de aparição). */
  protected readonly groups = computed<QueryGroup[]>(() => {
    const order: string[] = [];
    const map = new Map<string, QueryField[]>();
    for (const f of this.fields()) {
      const key = f.group ?? '';
      if (!map.has(key)) {
        map.set(key, []);
        order.push(key);
      }
      map.get(key)!.push(f);
    }
    return order.map((key) => ({
      group: key || null,
      label: key || 'Gerais',
      fields: map.get(key)!,
    }));
  });

  constructor() {
    this.api.listFlows().subscribe({
      next: (list) => this.policies.set(list.filter((f) => f.versions.some((v) => v.status === 'Published'))),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao carregar políticas.')),
    });
  }

  protected select(flowId: string): void {
    this.selectedId.set(flowId);
    this.result.set(null);
    this.error.set(null);
    this.fields.set([]);

    const flow = this.policies().find((f) => f.id === flowId);
    const published = flow?.versions.find((v) => v.status === 'Published');
    if (!flow || !published) return;

    this.loadingSchema.set(true);
    this.api.getInputSchema(flowId, published.id).subscribe({
      next: (schema) => {
        this.fields.set(this.toFields(schema));
        this.loadingSchema.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao carregar os campos da consulta.'));
        this.loadingSchema.set(false);
      },
    });
  }

  private toFields(schema: PolicyInputSchema): QueryField[] {
    return schema.fields.map((s: InputSchemaField) => ({
      name: s.name,
      label: s.label || this.leaf(s.name, s.group),
      type: (s.type.toLowerCase() as QueryField['type']) ?? 'text',
      required: s.required,
      group: s.group ?? null,
      value: '',
    }));
  }

  /** Nome-folha do campo (sem o prefixo do grupo), para o label padrão. */
  private leaf(name: string, group?: string | null): string {
    const g = (group ?? '').trim();
    return g && name.startsWith(g + '.') ? name.slice(g.length + 1) : name;
  }

  protected setValue(name: string, value: string): void {
    this.fields.update((fs) => fs.map((f) => (f.name === name ? { ...f, value } : f)));
  }

  protected selectedName(): string {
    return this.policies().find((f) => f.id === this.selectedId())?.name ?? '';
  }

  /** Trilha da resposta agrupada por política → categoria. */
  protected policyGroups(r: DecisionResponse): PolicyTraceGroup[] {
    return groupByPolicy(r.trace);
  }

  /** Relatório das Fontes (Fonte→Produto→dados, com origem online/cache). */
  protected sourcesOf(r: DecisionResponse): SourceReportGroup[] {
    return sourcesReport(r.trace);
  }

  protected outputEntries(r: DecisionResponse): { key: string; value: string }[] {
    return Object.entries(r.outputs).map(([key, value]) => ({ key, value }));
  }

  /** Campos informados na consulta (só os preenchidos), para o relatório. */
  protected informedFields(): { label: string; name: string; value: string }[] {
    return this.fields()
      .filter((f) => f.value.trim().length > 0)
      .map((f) => ({ label: f.label || f.name, name: f.name, value: f.value }));
  }

  /** Data/hora da consulta (preenchida ao receber o resultado). */
  protected readonly consultedAt = signal<Date | null>(null);

  /** Dispara a impressão (o navegador salva como PDF). */
  protected exportarPdf(): void {
    window.print();
  }

  protected consultar(): void {
    const flowId = this.selectedId();
    if (!flowId) return;

    // Valida obrigatórios antes de enviar (mesma regra do backend).
    const missing = this.fields()
      .filter((f) => f.required && !f.value.trim())
      .map((f) => f.label || f.name);
    if (missing.length > 0) {
      this.error.set('Preencha os campos obrigatórios: ' + missing.join(', ') + '.');
      return;
    }

    this.error.set(null);
    this.result.set(null);
    this.running.set(true);

    // Monta o payload achatado (o backend aceita achatado ou aninhado; enviamos
    // as chaves completas 'grupo.campo', que o motor resolve direto).
    const payload: Record<string, unknown> = {};
    for (const f of this.fields()) {
      if (f.value.trim()) payload[f.name] = this.coerce(f);
    }

    this.api.decide(flowId, this.reference.trim() || null, payload).subscribe({
      next: (r) => {
        this.result.set(r);
        this.consultedAt.set(new Date());
        this.running.set(false);
      },
      error: (e) => {
        this.error.set(apiErrorMessage(e, 'Falha ao executar a consulta.'));
        this.running.set(false);
      },
    });
  }

  private coerce(f: QueryField): unknown {
    switch (f.type) {
      case 'number':
        return Number(f.value);
      case 'boolean':
        return f.value === 'true';
      default:
        return f.value;
    }
  }
}

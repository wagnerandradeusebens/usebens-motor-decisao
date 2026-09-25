import { Component, Inject, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ApiService } from '../api/api.service';
import type { GraphInputField, InputFieldType, InputSchemaField } from '../api/models';

export interface RequestSchemaData {
  flowId: string;
  versionId: string;
  /** Campos manuais atuais (do editor). Editáveis aqui. */
  fields: GraphInputField[];
  readOnly: boolean;
}

export interface RequestSchemaResult {
  /** Campos manuais atualizados (o editor aplica no signal e salva). */
  fields: GraphInputField[];
}

const TYPES: { value: InputFieldType; label: string }[] = [
  { value: 'Number', label: 'número' },
  { value: 'Text', label: 'texto' },
  { value: 'Boolean', label: 'booleano' },
  { value: 'Date', label: 'data' },
];

/**
 * Tela de definição da REQUEST oficial da política: edita os campos manuais
 * (nome, rótulo, tipo, obrigatório, descrição, exemplo), lista os campos
 * derivados de fonte (travados, obrigatórios) e mostra um exemplo do payload do
 * POST /decisions. Opera sobre os campos manuais do editor (fonte única).
 */
@Component({
  selector: 'app-request-schema-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './request-schema-dialog.html',
  styleUrl: './request-schema-dialog.scss',
})
export class RequestSchemaDialog {
  private readonly api = inject(ApiService);
  private readonly snack = inject(MatSnackBar);

  protected readonly types = TYPES;
  /** Campos manuais editáveis (cópia local; devolvidos ao confirmar). */
  protected readonly fields = signal<GraphInputField[]>([]);
  /** Campos derivados de fonte (travados) e exemplo do payload — do schema. */
  protected readonly sourceFields = signal<InputSchemaField[]>([]);
  protected readonly exampleJson = signal<string>('{}');

  protected readonly hasSourceFields = computed(() => this.sourceFields().length > 0);

  constructor(
    private readonly ref: MatDialogRef<RequestSchemaDialog, RequestSchemaResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: RequestSchemaData,
  ) {
    this.fields.set(data.fields.map((f) => ({ ...f })));
    this.refreshSchema();
  }

  /** Carrega os campos de fonte + exemplo do backend (schema calculado). */
  private refreshSchema(): void {
    this.api.getInputSchema(this.data.flowId, this.data.versionId).subscribe({
      next: (schema) => {
        this.sourceFields.set(schema.fields.filter((f) => f.origin === 'Source'));
        this.exampleJson.set(schema.exampleRequestJson || '{}');
      },
      error: () => {
        this.sourceFields.set([]);
      },
    });
  }

  protected patch(i: number, patch: Partial<GraphInputField>): void {
    this.fields.update((fs) => fs.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  }

  /** Nome-folha do campo (sem o prefixo "grupo."). */
  protected leafOf(f: GraphInputField): string {
    const g = (f.group ?? '').trim();
    if (g && f.name.startsWith(g + '.')) return f.name.slice(g.length + 1);
    return f.name;
  }

  /** Recompõe o name completo a partir de grupo + folha. */
  private composeName(group: string, leaf: string): string {
    const g = group.trim();
    const l = leaf.trim();
    return g ? `${g}.${l}` : l;
  }

  /** Edita o grupo: atualiza group e recompõe o name (mantendo a folha). */
  protected setGroup(i: number, group: string): void {
    this.fields.update((fs) =>
      fs.map((f, j) => {
        if (j !== i) return f;
        const leaf = this.leafOf(f);
        return { ...f, group: group.trim() || null, name: this.composeName(group, leaf) };
      }),
    );
  }

  /** Edita a folha (nome técnico): recompõe o name (mantendo o grupo). */
  protected setLeaf(i: number, leaf: string): void {
    this.fields.update((fs) =>
      fs.map((f, j) => (j === i ? { ...f, name: this.composeName(f.group ?? '', leaf) } : f)),
    );
  }

  protected add(): void {
    this.fields.update((fs) => [
      ...fs,
      { name: '', label: '', type: 'Text', required: false, order: fs.length + 1, description: '', example: '', group: null },
    ]);
  }

  protected remove(i: number): void {
    this.fields.update((fs) => fs.filter((_, j) => j !== i));
  }

  protected copyExample(): void {
    navigator.clipboard?.writeText(this.exampleJson()).then(
      () => this.snack.open('Exemplo copiado.', 'ok', { duration: 2000 }),
      () => this.snack.open('Não foi possível copiar.', 'ok', { duration: 2500 }),
    );
  }

  protected confirm(): void {
    // Descarta linhas sem nome; reordena.
    const clean = this.fields()
      .filter((f) => this.leafOf(f).trim().length > 0)
      .map((f, i) => ({
        ...f,
        name: f.name.trim(),
        label: (f.label || this.leafOf(f)).trim(),
        group: (f.group ?? '').trim() || null,
        order: i + 1,
      }));
    this.ref.close({ fields: clean });
  }
}

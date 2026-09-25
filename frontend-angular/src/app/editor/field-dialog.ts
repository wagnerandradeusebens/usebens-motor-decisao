import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import type { InputFieldType } from '../api/models';

export interface FieldDialogData {
  mode: 'create' | 'edit';
  name: string;
  label: string;
  type: InputFieldType;
  required: boolean;
  readOnly: boolean;
}

export interface FieldResult {
  name: string;
  label: string;
  type: InputFieldType;
  required: boolean;
  deleted?: boolean;
}

const TYPES: { value: InputFieldType; label: string }[] = [
  { value: 'Number', label: 'número' },
  { value: 'Text', label: 'texto' },
  { value: 'Boolean', label: 'booleano' },
  { value: 'Date', label: 'data' },
];

/**
 * Modal para criar/editar um campo de entrada da proposta: nome técnico, rótulo,
 * tipo e obrigatoriedade. Mesmo padrão visual do modal de variável.
 */
@Component({
  selector: 'app-field-dialog',
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
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'create' ? 'Novo campo' : 'Editar campo' }}</h2>
    <mat-dialog-content>
      @if (confirmingDelete()) {
        <div class="confirm">
          <mat-icon>warning</mat-icon>
          <div>
            <strong>Excluir o campo "{{ data.label || data.name }}"?</strong>
            <div class="muted">Remove o campo do fluxo. Você ainda precisa Salvar para persistir.</div>
          </div>
        </div>
      }
      <mat-form-field appearance="outline" class="full">
        <mat-label>Nome técnico</mat-label>
        <input matInput [(ngModel)]="name" [disabled]="data.readOnly"
               placeholder="ex.: renda_mensal" autocomplete="off" />
        <mat-hint>Usado nas fórmulas como <code>{{ "'" + (name || 'campo') + "'" }}</code>.</mat-hint>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-label>Rótulo</mat-label>
        <input matInput [(ngModel)]="label" [disabled]="data.readOnly"
               placeholder="ex.: Renda mensal" autocomplete="off" />
      </mat-form-field>

      <div class="row">
        <mat-form-field appearance="outline" class="type">
          <mat-label>Tipo</mat-label>
          <mat-select [(ngModel)]="type" [disabled]="data.readOnly">
            @for (t of types; track t.value) {
              <mat-option [value]="t.value">{{ t.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-checkbox [(ngModel)]="required" [disabled]="data.readOnly">obrigatório</mat-checkbox>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions>
      @if (data.mode === 'edit' && !data.readOnly) {
        @if (!confirmingDelete()) {
          <button matButton class="danger" (click)="confirmingDelete.set(true)">
            <mat-icon>delete</mat-icon> Excluir
          </button>
        } @else {
          <button matButton class="danger" (click)="delete()">
            <mat-icon>delete_forever</mat-icon> Confirmar exclusão
          </button>
          <button matButton (click)="confirmingDelete.set(false)">Cancelar exclusão</button>
        }
      }
      <span class="spacer"></span>
      <button matButton mat-dialog-close>Cancelar</button>
      <button matButton="filled" (click)="confirm()" [disabled]="data.readOnly || !name.trim() || confirmingDelete()">
        {{ data.mode === 'create' ? 'Criar' : 'Salvar' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: 640px; max-width: 100%; padding-top: 1.75rem; }
    .full { width: 100%; display: block; }
    .row { display: flex; align-items: center; gap: 1rem; }
    .type { flex: 1; }
    .spacer { flex: 1 1 auto; }
    .danger { color: var(--cor-perigo); }
    .confirm {
      display: flex; align-items: flex-start; gap: 0.6rem;
      background: #fef2f2; border: 1px solid #fecaca; color: #991b1b;
      padding: 0.7rem 0.85rem; border-radius: 8px; margin-bottom: 1rem;
    }
    .confirm .muted { color: #b45c5c; font-size: 0.85rem; margin-top: 0.15rem; }
  `,
})
export class FieldDialog {
  protected readonly types = TYPES;
  protected name: string;
  protected label: string;
  protected type: InputFieldType;
  protected required: boolean;
  protected readonly confirmingDelete = signal(false);

  constructor(
    private readonly ref: MatDialogRef<FieldDialog, FieldResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: FieldDialogData,
  ) {
    this.name = data.name;
    this.label = data.label;
    this.type = data.type;
    this.required = data.required;
  }

  protected confirm(): void {
    const name = this.name.trim();
    if (!name) return;
    this.ref.close({ name, label: this.label.trim() || name, type: this.type, required: this.required });
  }

  protected delete(): void {
    this.ref.close({
      name: this.data.name,
      label: this.data.label,
      type: this.data.type,
      required: this.data.required,
      deleted: true,
    });
  }
}

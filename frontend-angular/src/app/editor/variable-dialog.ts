import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkDrag, CdkDragHandle } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import type { SourceDescriptorDto } from '../api/models';
import { FormulaInput } from './formula-input';

export interface VariableDialogData {
  /** 'create' para nova variável, 'edit' para editar existente. */
  mode: 'create' | 'edit';
  key: string;
  expression: string;
  readOnly: boolean;
  /** Listas para o autocomplete de fórmula. */
  fields: string[];
  variables: string[];
  sources: SourceDescriptorDto[];
  /** Nomes de todas as políticas publicadas (gatilho '(' de referência). */
  policies: string[];
}

export interface VariableResult {
  key: string;
  expression: string;
  /** Quando true, o editor deve remover a variável (ação "Excluir" do modal). */
  deleted?: boolean;
}

/**
 * Modal para criar/editar uma variável local: uma linha para o nome e um campo
 * de texto maior abaixo para o conteúdo (fórmula). Ao confirmar, devolve
 * { key, expression } para o editor persistir na lista de variáveis.
 */
@Component({
  selector: 'app-variable-dialog',
  imports: [
    FormsModule,
    CdkDrag,
    CdkDragHandle,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    FormulaInput,
  ],
  template: `
    <div class="resizable" cdkDrag cdkDragRootElement=".cdk-overlay-pane" [cdkDragDisabled]="false">
      <h2 mat-dialog-title cdkDragHandle class="drag-title">
        {{ data.mode === 'create' ? 'Nova variável' : 'Editar variável' }}
        <mat-icon class="drag-grip" title="Arraste para mover">drag_indicator</mat-icon>
      </h2>
      <mat-dialog-content>
      @if (confirmingDelete()) {
        <div class="confirm">
          <mat-icon>warning</mat-icon>
          <div>
            <strong>Excluir a variável "{{ data.key }}"?</strong>
            <div class="muted">Esta ação remove a variável do fluxo. Você ainda precisa Salvar para persistir.</div>
          </div>
        </div>
      }
      <mat-form-field appearance="outline" class="full">
        <mat-label>Nome da variável</mat-label>
        <input
          matInput
          [(ngModel)]="key"
          [disabled]="data.readOnly"
          placeholder="ex.: comprometimento"
          autocomplete="off"
        />
        <mat-hint>Referencie nas fórmulas como <code>{{ '{' + (key || 'nome') + '}' }}</code>.</mat-hint>
      </mat-form-field>

      <label class="field-label">Conteúdo (fórmula)</label>
      <app-formula-input
        [value]="expression"
        (valueChange)="expression = $event"
        [fields]="data.fields"
        [variables]="data.variables"
        [sources]="data.sources"
        [policies]="data.policies"
        [disabled]="data.readOnly"
        [rows]="7"
        placeholder="ex.: 'divida' / 'renda'  ·  {variavel}  ·  [SERASA;Score;Pontuacao]"
      ></app-formula-input>
      <div class="formula-help">
        Dica: <code>'</code> campos · <code>&#123;</code> variáveis ·
        <code>[</code> fontes · <code>(</code> política (pontos/limite/resposta) · letras para funções.
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
      <button matButton="filled" (click)="confirm()" [disabled]="data.readOnly || !key.trim() || confirmingDelete()">
        {{ data.mode === 'create' ? 'Criar' : 'Salvar' }}
      </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: `
    /* Container redimensionável: o usuário arrasta o canto inferior direito. */
    .resizable {
      display: flex;
      flex-direction: column;
      resize: both;
      overflow: auto;
      min-width: 420px;
      min-height: 320px;
      max-width: 92vw;
      max-height: 90vh;
      width: 680px;
    }
    mat-dialog-content { flex: 1 1 auto; padding-top: 1.5rem; }
    .drag-title {
      display: flex; align-items: center; justify-content: space-between;
      cursor: move; user-select: none; margin-bottom: 0;
    }
    .drag-grip { color: var(--cor-texto-claro); }
    .full { width: 100%; display: block; }
    .field-label {
      display: block; font-size: 0.8rem; font-weight: 500;
      color: var(--cor-texto-claro); margin: 0.25rem 0 0.35rem;
    }
    .formula-help { font-size: 0.75rem; color: var(--cor-texto-claro); margin-top: 0.4rem; }
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
export class VariableDialog {
  protected key: string;
  protected expression: string;
  protected readonly confirmingDelete = signal(false);

  constructor(
    private readonly ref: MatDialogRef<VariableDialog, VariableResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: VariableDialogData,
  ) {
    this.key = data.key;
    this.expression = data.expression;
  }

  protected confirm(): void {
    const key = this.key.trim();
    if (!key) return;
    this.ref.close({ key, expression: this.expression });
  }

  /** Confirma a exclusão (fecha o modal sinalizando remoção ao editor). */
  protected delete(): void {
    this.ref.close({ key: this.data.key, expression: this.data.expression, deleted: true });
  }
}

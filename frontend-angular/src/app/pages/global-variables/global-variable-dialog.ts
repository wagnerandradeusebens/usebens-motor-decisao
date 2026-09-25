import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import type { GlobalVariable } from '../../api/models';

export interface GlobalVariableResult {
  key: string;
  expression: string;
}

/** Diálogo de criar/editar variável global (chave + fórmula). */
@Component({
  selector: 'app-global-variable-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>{{ data ? 'Editar variável global' : 'Nova variável global' }}</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Nome da variável</mat-label>
        <input matInput [(ngModel)]="key" placeholder="ex.: idade_minima" />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Fórmula</mat-label>
        <textarea matInput rows="4" [(ngModel)]="expression"
          placeholder="ex.: 18   ou   ARRED('renda' * 0.3; 2)"></textarea>
      </mat-form-field>
      <p class="hint muted">
        Campos <code>'campo'</code>, variáveis <code>{{ '{variavel}' }}</code>, texto
        <code>"texto"</code>, fontes <code>[Fonte;Produto;Dado]</code> e funções (SE, ARRED…).
        Separador: <code>;</code>
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancelar</button>
      <button matButton="filled" [disabled]="!key.trim()" (click)="save()">Salvar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; display: block; }
    .hint { font-size: 0.78rem; }
    code { font-family: ui-monospace, monospace; }
  `,
})
export class GlobalVariableDialog {
  protected key = '';
  protected expression = '';

  constructor(
    private readonly ref: MatDialogRef<GlobalVariableDialog, GlobalVariableResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: GlobalVariable | null,
  ) {
    if (data) {
      this.key = data.key;
      this.expression = data.expression;
    }
  }

  protected save(): void {
    const key = this.key.trim();
    if (!key) return;
    this.ref.close({ key, expression: this.expression });
  }
}

import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface NewPolicyResult {
  name: string;
  description: string | null;
}

/** Diálogo "Nova política": nome + descrição. Devolve o resultado no close. */
@Component({
  selector: 'app-new-policy-dialog',
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Nova política</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Nome</mat-label>
        <input matInput [(ngModel)]="name" placeholder="Ex.: Crédito auto - pessoa física" />
      </mat-form-field>
      <mat-form-field appearance="outline" class="full">
        <mat-label>Descrição</mat-label>
        <input matInput [(ngModel)]="description" placeholder="Opcional" />
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancelar</button>
      <button matButton="filled" [disabled]="!name.trim()" (click)="create()">Criar</button>
    </mat-dialog-actions>
  `,
  styles: `.full { width: 100%; display: block; }`,
})
export class NewPolicyDialog {
  protected name = '';
  protected description = '';

  constructor(private readonly ref: MatDialogRef<NewPolicyDialog, NewPolicyResult>) {}

  protected create(): void {
    const name = this.name.trim();
    if (!name) return;
    this.ref.close({ name, description: this.description.trim() || null });
  }
}

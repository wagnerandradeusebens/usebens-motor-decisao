import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import type { DecisionOutcome } from '../api/models';

export type BranchTarget =
  | { kind: 'node'; nodeKey: string }
  | { kind: 'outcome'; outcome: DecisionOutcome };

export interface BranchData {
  branch: 'true' | 'false';
  nodeOptions: { key: string; label: string }[];
}

const OUTCOMES: { value: DecisionOutcome; label: string }[] = [
  { value: 'Approved', label: 'Aprovar' },
  { value: 'ApprovedWithConditions', label: 'Aprovar com condições' },
  { value: 'ManualReview', label: 'Revisão manual' },
  { value: 'Denied', label: 'Negar' },
];

/**
 * Define o destino de uma saída (Verdadeiro/Falso) de uma condição: ou vai para
 * um nó existente, ou alcança um desfecho terminal (o editor cria um nó Decisão
 * + aresta para manter o grafo válido). Espelha o BranchModal do React.
 */
@Component({
  selector: 'app-branch-dialog',
  imports: [
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatFormFieldModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.branch === 'true' ? 'Saída: Verdadeiro' : 'Saída: Falso' }}</h2>
    <mat-dialog-content>
      <mat-button-toggle-group [value]="mode()" (change)="mode.set($event.value)" class="modes">
        <mat-button-toggle value="outcome">Desfecho</mat-button-toggle>
        <mat-button-toggle value="node" [disabled]="data.nodeOptions.length === 0">Ir para nó</mat-button-toggle>
      </mat-button-toggle-group>

      @if (mode() === 'outcome') {
        <mat-form-field appearance="outline" class="full">
          <mat-label>Desfecho desta saída</mat-label>
          <mat-select [(ngModel)]="outcome">
            @for (o of outcomes; track o.value) {
              <mat-option [value]="o.value">{{ o.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      } @else {
        <mat-form-field appearance="outline" class="full">
          <mat-label>Nó de destino</mat-label>
          <mat-select [(ngModel)]="nodeKey">
            @for (n of data.nodeOptions; track n.key) {
              <mat-option [value]="n.key">{{ n.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancelar</button>
      <button matButton="filled" (click)="confirm()" [disabled]="mode() === 'node' && !nodeKey">Confirmar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .modes { margin-bottom: 1rem; }
    .full { width: 100%; display: block; }
    mat-dialog-content { min-width: 420px; }
  `,
})
export class BranchDialog {
  protected readonly outcomes = OUTCOMES;
  protected readonly mode = signal<'outcome' | 'node'>('outcome');
  protected outcome: DecisionOutcome = 'Approved';
  protected nodeKey: string;

  constructor(
    private readonly ref: MatDialogRef<BranchDialog, BranchTarget>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: BranchData,
  ) {
    this.nodeKey = data.nodeOptions[0]?.key ?? '';
  }

  protected confirm(): void {
    const target: BranchTarget =
      this.mode() === 'outcome'
        ? { kind: 'outcome', outcome: this.outcome }
        : { kind: 'node', nodeKey: this.nodeKey };
    this.ref.close(target);
  }
}

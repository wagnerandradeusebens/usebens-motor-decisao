import { Component, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import {
  EFConnectableSide,
  FCanvasComponent,
  FCreateConnectionEvent,
  FCreateNodeEvent,
  FFlowModule,
} from '@foblex/flow';
import { IPoint } from '@foblex/2d';
import { ApiService } from '../api/api.service';
import type {
  DecisionOutcome,
  FlowNodeKind,
  FlowVersionStatus,
  GraphEdge,
  GraphRule,
  GraphRuleset,
  VersionGraph,
  GraphFormula,
  GraphInputField,
  InputFieldType,
  RuleEffect,
} from '../api/models';
import { apiErrorMessage } from '../shared/format';
import { defaultConfig, OUTCOME_LABEL, RULE_PALETTE } from './node-config';
import { NodeConfigDialog, NodeConfigData, NodeConfigResult } from './node-config-dialog';
import { BranchDialog, BranchData, BranchTarget } from './branch-dialog';
import { DecisionRunnerDialog, DecisionRunnerData } from './decision-runner-dialog';

interface EditorNode {
  id: string;
  kind: FlowNodeKind;
  label: string;
  x: number;
  y: number;
  config: string;
  rulesetKey: string | null;
}

interface EditorEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string | null;
  label: string | null;
}

/**
 * Editor visual de fluxo baseado em @foblex/flow. Carrega o grafo da versão,
 * renderiza nós por tipo no canvas com conectores, permite arrastar da paleta,
 * mover nós, criar/remover conexões, configurar via duplo-clique (diálogo),
 * salvar, publicar e testar. Versões publicadas/arquivadas ficam somente leitura.
 */
@Component({
  selector: 'app-graph-editor',
  imports: [
    RouterLink,
    FormsModule,
    FFlowModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatDialogModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
  ],
  templateUrl: './graph-editor.html',
  styleUrl: './graph-editor.scss',
})
export class GraphEditor {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  protected readonly flowId = this.route.snapshot.paramMap.get('flowId')!;
  protected readonly versionId = this.route.snapshot.paramMap.get('versionId')!;

  protected readonly nodes = signal<EditorNode[]>([]);
  protected readonly edges = signal<EditorEdge[]>([]);
  protected readonly status = signal<FlowVersionStatus>('Draft');
  protected readonly error = signal<string | null>(null);
  protected readonly palette = RULE_PALETTE;
  protected readonly side = EFConnectableSide;

  // Partes do grafo que o editor visual não desenha, mas precisa preservar/editar.
  protected readonly rulesets = signal<GraphRuleset[]>([]);
  protected readonly inputFields = signal<GraphInputField[]>([]);
  protected readonly formulas = signal<GraphFormula[]>([]);

  // Opções para os selects dos editores laterais.
  protected readonly effects: RuleEffect[] = ['Score', 'Decision', 'Annotation'];
  protected readonly fieldTypes: InputFieldType[] = ['Number', 'Text', 'Boolean', 'Date'];
  protected readonly fieldTypeLabel: Record<InputFieldType, string> = {
    Number: 'número', Text: 'texto', Boolean: 'booleano', Date: 'data',
  };
  protected readonly branchOutcomes: DecisionOutcome[] = ['Approved', 'ApprovedWithConditions', 'ManualReview', 'Denied'];

  private readonly canvas = viewChild(FCanvasComponent);

  protected get readOnly(): boolean {
    return this.status() !== 'Draft';
  }

  constructor() {
    this.load();
  }

  protected onLoaded(): void {
    this.canvas()?.fitToScreen({ x: 60, y: 60 } as IPoint, false);
  }

  private load(): void {
    this.api.getFlow(this.flowId).subscribe({
      next: (flow) => {
        const v = flow.versions.find((x) => x.id === this.versionId);
        this.status.set(v?.status ?? 'Draft');
      },
    });

    this.api.getVersionGraph(this.flowId, this.versionId).subscribe({
      next: (graph) => this.applyGraph(graph),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao carregar a versão.')),
    });
  }

  private applyGraph(graph: VersionGraph): void {
    this.rulesets.set(graph.rulesets);
    this.formulas.set(graph.formulas);
    this.inputFields.set(graph.inputFields ?? []);

    let nodes: EditorNode[] = graph.nodes.map((n) => ({
      id: n.nodeKey,
      kind: n.kind,
      label: n.label,
      x: n.positionX,
      y: n.positionY,
      config: n.config,
      rulesetKey: n.rulesetKey,
    }));

    // Toda política tem exatamente um Start; cria um se o grafo estiver vazio.
    if (nodes.length === 0) {
      nodes = [{ id: 'start', kind: 'Start', label: 'Início', x: 300, y: 40, config: '{}', rulesetKey: null }];
    }

    this.nodes.set(nodes);
    this.edges.set(
      graph.edges.map((e) => ({
        id: e.edgeKey,
        source: e.sourceNodeKey,
        target: e.targetNodeKey,
        sourceHandle: e.sourceHandle,
        label: e.label ?? e.sourceHandle,
      })),
    );
  }

  // --- Conectores por tipo de nó ---------------------------------------
  protected hasInput(kind: FlowNodeKind): boolean {
    return kind !== 'Start' && kind !== 'Comment';
  }
  protected hasOutput(kind: FlowNodeKind): boolean {
    return kind !== 'Decision' && kind !== 'Comment';
  }
  protected isCondition(kind: FlowNodeKind): boolean {
    return kind === 'Condition';
  }

  // --- Interações -------------------------------------------------------

  /** Recebe um item arrastado da paleta (fExternalItem) e cria o nó na posição solta. */
  protected onCreateNode(event: FCreateNodeEvent): void {
    if (this.readOnly) return;
    const kind = event.data as FlowNodeKind;
    if (!kind) return;
    const pos = event.dropPosition ?? { x: event.externalItemRect?.x ?? 0, y: event.externalItemRect?.y ?? 0 };
    const id = `${kind.toLowerCase()}-${Date.now()}`;
    this.nodes.update((ns) => [
      ...ns,
      {
        id,
        kind,
        label: kind,
        x: Math.round(pos.x),
        y: Math.round(pos.y),
        config: defaultConfig(kind),
        rulesetKey: null,
      },
    ]);
  }

  /** Atualiza a posição de um nó após mover no canvas. */
  protected onNodePositionChanged(id: string, position: IPoint): void {
    this.nodes.update((ns) => ns.map((n) => (n.id === id ? { ...n, x: position.x, y: position.y } : n)));
  }

  /** Cria uma conexão entre dois conectores (sourceId "<nodeId>" ou "<nodeId>:true|false"). */
  protected onCreateConnection(event: FCreateConnectionEvent): void {
    if (this.readOnly || !event.targetId) return;
    const [sourceNode, handle] = event.sourceId.split(':');
    const target = event.targetId.split(':')[0];
    this.edges.update((es) => [
      ...es.filter((e) => !(e.source === sourceNode && (e.sourceHandle ?? '') === (handle ?? ''))),
      {
        id: `e-${Date.now()}`,
        source: sourceNode,
        target,
        sourceHandle: handle ?? null,
        label: handle === 'true' ? 'V' : handle === 'false' ? 'F' : null,
      },
    ]);
  }

  protected openConfig(node: EditorNode): void {
    const data: NodeConfigData = {
      kind: node.kind,
      label: node.label,
      config: node.config,
      readOnly: this.readOnly,
    };
    const ref = this.dialog.open<NodeConfigDialog, NodeConfigData, NodeConfigResult>(NodeConfigDialog, {
      data,
      width: '640px',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      if (result.deleted) {
        this.deleteNode(node.id);
        return;
      }
      this.nodes.update((ns) =>
        ns.map((n) => (n.id === node.id ? { ...n, label: result.label, config: result.config } : n)),
      );
      if (result.configureBranch) {
        this.openBranch(node.id, result.configureBranch);
      }
    });
  }

  /** Abre o diálogo de destino de uma saída (V/F) de uma condição. */
  private openBranch(nodeKey: string, branch: 'true' | 'false'): void {
    const data: BranchData = {
      branch,
      nodeOptions: this.nodes()
        .filter((n) => n.id !== nodeKey && n.kind !== 'Comment')
        .map((n) => ({ key: n.id, label: `${n.label} (${n.kind})` })),
    };
    const ref = this.dialog.open<BranchDialog, BranchData, BranchTarget>(BranchDialog, { data, width: '460px' });
    ref.afterClosed().subscribe((target) => {
      if (target) this.applyBranch(nodeKey, branch, target);
    });
  }

  /** Cria a aresta da saída V/F para um nó existente ou um novo nó de decisão. */
  private applyBranch(nodeKey: string, branch: 'true' | 'false', target: BranchTarget): void {
    let targetKey: string;
    if (target.kind === 'node') {
      targetKey = target.nodeKey;
    } else {
      targetKey = `decision-${Date.now()}`;
      const src = this.nodes().find((n) => n.id === nodeKey);
      const x = (src?.x ?? 0) + (branch === 'true' ? -140 : 160);
      const y = (src?.y ?? 0) + 150;
      this.nodes.update((ns) => [
        ...ns,
        {
          id: targetKey,
          kind: 'Decision',
          label: OUTCOME_LABEL[target.outcome],
          x,
          y,
          config: JSON.stringify({ outcome: target.outcome }),
          rulesetKey: null,
        },
      ]);
    }
    this.edges.update((es) => [
      ...es.filter((e) => !(e.source === nodeKey && (e.sourceHandle ?? '') === branch)),
      {
        id: `e-${Date.now()}`,
        source: nodeKey,
        target: targetKey,
        sourceHandle: branch,
        label: branch === 'true' ? 'V' : 'F',
      },
    ]);
  }

  private deleteNode(id: string): void {
    const node = this.nodes().find((n) => n.id === id);
    if (!node || node.kind === 'Start') return;
    this.nodes.update((ns) => ns.filter((n) => n.id !== id));
    this.edges.update((es) => es.filter((e) => e.source !== id && e.target !== id));
  }

  // --- Persistência -----------------------------------------------------

  private currentGraph(): VersionGraph {
    const nodes = this.nodes().map((n) => ({
      nodeKey: n.id,
      kind: n.kind,
      label: n.label,
      positionX: n.x,
      positionY: n.y,
      config: n.config,
      rulesetKey: n.rulesetKey,
    }));
    const edges: GraphEdge[] = this.edges().map((e) => ({
      edgeKey: e.id,
      sourceNodeKey: e.source,
      targetNodeKey: e.target,
      sourceHandle: e.sourceHandle,
      label: e.label,
    }));
    return { nodes, edges, rulesets: this.rulesets(), formulas: this.formulas(), inputFields: this.inputFields() };
  }

  // --- Editor lateral: Minhas Variáveis (locais) -----------------------
  protected addFormula(): void {
    this.formulas.update((fs) => [...fs, { key: '', label: '', expression: '' }]);
  }
  protected patchFormula(i: number, patch: Partial<GraphFormula>): void {
    this.formulas.update((fs) =>
      fs.map((f, j) => {
        if (j !== i) return f;
        const next = { ...f, ...patch };
        // O rótulo acompanha a chave por padrão (como no editor React).
        if (patch.key !== undefined) next.label = patch.key;
        return next;
      }),
    );
  }
  protected removeFormula(i: number): void {
    this.formulas.update((fs) => fs.filter((_, j) => j !== i));
  }

  // --- Editor lateral: Conjuntos de regras (Ruleset) -------------------
  protected addRuleset(): void {
    this.rulesets.update((rs) => [
      ...rs,
      { rulesetKey: `rs-${Date.now()}`, name: 'Novo conjunto', description: null, approvalThreshold: null, rules: [] },
    ]);
  }
  protected patchRuleset(i: number, patch: Partial<GraphRuleset>): void {
    this.rulesets.update((rs) => rs.map((x, j) => (j === i ? { ...x, ...patch } : x)));
  }
  protected addRule(i: number): void {
    this.rulesets.update((rs) =>
      rs.map((x, j) =>
        j === i
          ? {
              ...x,
              rules: [
                ...x.rules,
                { order: x.rules.length + 1, name: 'Nova regra', conditionExpression: '', effect: 'Score', scoreWeight: 0, forcedOutcome: null, message: null, isEnabled: true },
              ],
            }
          : x,
      ),
    );
  }
  protected patchRule(rsIdx: number, ruleIdx: number, patch: Partial<GraphRule>): void {
    this.rulesets.update((rs) =>
      rs.map((x, j) =>
        j === rsIdx ? { ...x, rules: x.rules.map((r, k) => (k === ruleIdx ? { ...r, ...patch } : r)) } : x,
      ),
    );
  }
  protected removeRule(rsIdx: number, ruleIdx: number): void {
    this.rulesets.update((rs) =>
      rs.map((x, j) => (j === rsIdx ? { ...x, rules: x.rules.filter((_, k) => k !== ruleIdx) } : x)),
    );
  }
  protected toNumber(v: string): number {
    return Number(v) || 0;
  }

  // --- Editor lateral: Campos de entrada -------------------------------
  protected addField(): void {
    this.inputFields.update((fs) => [
      ...fs,
      { name: '', label: '', type: 'Number', required: false, order: fs.length + 1 },
    ]);
  }
  protected patchField(i: number, patch: Partial<GraphInputField>): void {
    this.inputFields.update((fs) => fs.map((f, j) => (j === i ? { ...f, ...patch } : f)));
  }
  protected removeField(i: number): void {
    this.inputFields.update((fs) => fs.filter((_, j) => j !== i));
  }

  protected save(): void {
    this.error.set(null);
    this.api.saveVersionGraph(this.flowId, this.versionId, this.currentGraph()).subscribe({
      next: () => this.snack.open('Rascunho salvo.', 'ok', { duration: 2500 }),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao salvar.')),
    });
  }

  protected publish(): void {
    this.error.set(null);
    this.api.saveVersionGraph(this.flowId, this.versionId, this.currentGraph()).subscribe({
      next: () => {
        this.api.publishVersion(this.flowId, this.versionId).subscribe({
          next: (v) => {
            this.status.set(v.status);
            this.snack.open('Versão publicada.', 'ok', { duration: 2500 });
          },
          error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao publicar.')),
        });
      },
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao salvar antes de publicar.')),
    });
  }

  protected createDraft(): void {
    this.error.set(null);
    this.api.createVersion(this.flowId, this.versionId).subscribe({
      next: (v) => this.router.navigate(['/politicas', this.flowId, 'versions', v.id]),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao criar rascunho.')),
    });
  }

  protected openTest(): void {
    const data: DecisionRunnerData = { flowId: this.flowId, inputFields: this.inputFields() };
    this.dialog.open<DecisionRunnerDialog, DecisionRunnerData>(DecisionRunnerDialog, { data, width: '720px' });
  }
}

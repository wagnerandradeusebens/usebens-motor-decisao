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
  EFMarkerType,
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
  SourceDescriptorDto,
} from '../api/models';
import { apiErrorMessage } from '../shared/format';
import { defaultConfig, OUTCOME_LABEL, RULE_PALETTE } from './node-config';
import { FUNCTIONS } from './function-catalog';
import { NodeConfigDialog, NodeConfigData, NodeConfigResult } from './node-config-dialog';
import { BranchDialog, BranchData, BranchTarget } from './branch-dialog';
import { DecisionRunnerDialog, DecisionRunnerData } from './decision-runner-dialog';
import { VariableDialog, VariableDialogData, VariableResult } from './variable-dialog';
import { FieldDialog, FieldDialogData, FieldResult } from './field-dialog';

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
  protected readonly markerType = EFMarkerType;
  protected readonly functions = FUNCTIONS;

  /** Referência dos operadores da linguagem de fórmulas (read-only). */
  protected readonly operators: { symbol: string; description: string }[] = [
    { symbol: '+  -  *  /', description: 'Aritméticos' },
    { symbol: '^', description: 'Potência' },
    { symbol: '&', description: 'Concatenação de texto' },
    { symbol: '=  <>', description: 'Igual / diferente' },
    { symbol: '<  <=  >  >=', description: 'Comparações' },
    { symbol: ';', description: 'Separador de argumentos' },
  ];

  // Partes do grafo que o editor visual não desenha, mas precisa preservar/editar.
  protected readonly rulesets = signal<GraphRuleset[]>([]);
  protected readonly inputFields = signal<GraphInputField[]>([]);
  protected readonly formulas = signal<GraphFormula[]>([]);
  protected readonly sources = signal<SourceDescriptorDto[]>([]);
  protected readonly policyName = signal<string>('Política atual');
  /** Nomes de todas as políticas (para o autocomplete de referência com '('). */
  protected readonly policies = signal<string[]>([]);

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

  /** Chamado quando o flow termina de renderizar (fFullRendered). */
  protected onLoaded(): void {
    this.centerGraph();
  }

  /**
   * Ajusta o zoom para caber todo o grafo e o centraliza na viewport. Precisa
   * rodar depois que os nós estão renderizados/medidos — daí o defer por frame,
   * usado tanto no fFullRendered quanto após carregar o grafo da API.
   */
  private centerGraph(): void {
    setTimeout(() => this.canvas()?.fitToScreen({ x: 80, y: 80 } as IPoint, false), 0);
  }

  private load(): void {
    this.api.getFlow(this.flowId).subscribe({
      next: (flow) => {
        const v = flow.versions.find((x) => x.id === this.versionId);
        this.status.set(v?.status ?? 'Draft');
        this.policyName.set(flow.name);
      },
    });

    this.api.getVersionGraph(this.flowId, this.versionId).subscribe({
      next: (graph) => this.applyGraph(graph),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao carregar a versão.')),
    });

    // Catálogo de fontes externas para o autocomplete ([Fonte;Produto;Dado]).
    this.api.listSources().subscribe({
      next: (list) => this.sources.set(list),
      error: () => this.sources.set([]),
    });

    // Todas as políticas, para o autocomplete de referência cruzada com '('.
    this.api.listFlows().subscribe({
      next: (list) => this.policies.set(list.map((f) => f.name).filter((n) => !!n)),
      error: () => this.policies.set([]),
    });
  }

  /** Nomes das variáveis locais (para o autocomplete de {variavel}). */
  private variableNames(): string[] {
    return this.formulas().map((f) => f.key).filter((k) => !!k);
  }
  /** Nomes técnicos dos campos (para o autocomplete de 'campo'). */
  private fieldNames(): string[] {
    return this.inputFields().map((f) => f.name).filter((n) => !!n);
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

    // Os dados chegam depois do fFullRendered inicial; recentraliza agora que os
    // nós reais estão no canvas, para o grafo abrir enquadrado e centralizado.
    this.centerGraph();
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

  /**
   * Metadados visuais por tipo de nó: rótulo em pt-BR e ícone (Material Symbols).
   * A forma é dada pela classe do kind no CSS. Baseado nos blocos do Crivo.
   */
  private static readonly NODE_META: Record<FlowNodeKind, { label: string; icon: string }> = {
    Start: { label: 'Início', icon: 'play_circle' },
    Condition: { label: 'Condição', icon: 'help' },
    Ruleset: { label: 'Conjunto de regras', icon: 'rule' },
    Computation: { label: 'Cálculo', icon: 'calculate' },
    DataSource: { label: 'Fonte de dados', icon: 'cloud' },
    Decision: { label: 'Decisão', icon: 'flag' },
    Action: { label: 'Ação', icon: 'bolt' },
    Matrix: { label: 'Regra matriz', icon: 'grid_on' },
    Comment: { label: 'Comentário', icon: 'sticky_note_2' },
  };

  protected nodeLabel(kind: FlowNodeKind): string {
    return GraphEditor.NODE_META[kind]?.label ?? kind;
  }
  protected nodeIcon(kind: FlowNodeKind): string {
    return GraphEditor.NODE_META[kind]?.icon ?? 'crop_square';
  }

  /**
   * Resumo legível do que o nó faz, extraído da sua config JSON, para exibir no
   * próprio bloco (como no Crivo) sem o usuário precisar abrir o nó.
   */
  protected nodeSummary(node: EditorNode): string {
    let cfg: Record<string, unknown> = {};
    try {
      cfg = JSON.parse(node.config || '{}');
    } catch {
      return '';
    }
    switch (node.kind) {
      case 'Condition': {
        const expr = (cfg['expression'] as string) ?? '';
        return expr.trim() || 'sem condição';
      }
      case 'Decision': {
        const outcome = cfg['outcome'] as DecisionOutcome | undefined;
        return outcome ? `→ ${OUTCOME_LABEL[outcome] ?? outcome}` : 'sem desfecho';
      }
      case 'DataSource': {
        const src = (cfg['source'] as string) ?? '';
        return src.trim() || 'sem fonte';
      }
      case 'Computation': {
        const assignments = (cfg['assignments'] as unknown[]) ?? [];
        if (assignments.length === 0) return 'sem cálculo';
        return `${assignments.length} atribuição(ões)`;
      }
      case 'Action': {
        const actions = (cfg['actions'] as unknown[]) ?? [];
        return actions.length === 0 ? 'sem ações' : `${actions.length} ação(ões)`;
      }
      case 'Matrix': {
        const rows = (cfg['rowBands'] as unknown[])?.length ?? 0;
        const cols = (cfg['colBands'] as unknown[])?.length ?? 0;
        return rows && cols ? `${rows} × ${cols} faixas` : 'matriz vazia';
      }
      case 'Ruleset': {
        return node.rulesetKey
          ? this.rulesets().find((r) => r.rulesetKey === node.rulesetKey)?.name ?? 'conjunto'
          : 'sem conjunto';
      }
      default:
        return '';
    }
  }

  /**
   * Auto-organiza os nós numa árvore de cima para baixo (Start no topo), por
   * níveis. Usa BFS a partir do Start para atribuir a linha de cada nó e
   * distribui os irmãos horizontalmente centralizados. Nós sem caminho a partir
   * do Start (soltos) vão para uma linha extra ao final. Não salva sozinho — o
   * usuário revê e clica em "Salvar".
   */
  protected autoLayout(): void {
    if (this.readOnly) return;

    const nodes = this.nodes();
    if (nodes.length === 0) return;

    const V_GAP = 170; // distância vertical entre níveis
    const H_GAP = 220; // distância horizontal entre irmãos

    // Adjacência a partir das arestas (na ordem: true antes de false).
    const children = new Map<string, string[]>();
    for (const n of nodes) children.set(n.id, []);
    const ordered = [...this.edges()].sort((a, b) => {
      const rank = (h: string | null) => (h === 'true' ? 0 : h === 'false' ? 1 : 0);
      return rank(a.sourceHandle) - rank(b.sourceHandle);
    });
    for (const e of ordered) {
      if (children.has(e.source) && children.has(e.target)) {
        children.get(e.source)!.push(e.target);
      }
    }

    // Raiz: o Start; se não houver, o primeiro nó.
    const start = nodes.find((n) => n.kind === 'Start') ?? nodes[0];

    // BFS para atribuir níveis (evita ciclos com o visited).
    const level = new Map<string, number>();
    const queue: string[] = [start.id];
    level.set(start.id, 0);
    while (queue.length) {
      const id = queue.shift()!;
      const lvl = level.get(id)!;
      for (const child of children.get(id) ?? []) {
        if (!level.has(child)) {
          level.set(child, lvl + 1);
          queue.push(child);
        }
      }
    }

    // Nós não alcançados a partir do Start vão para um nível extra ao final.
    let maxLevel = 0;
    for (const l of level.values()) maxLevel = Math.max(maxLevel, l);
    for (const n of nodes) {
      if (!level.has(n.id)) level.set(n.id, maxLevel + 1);
    }

    // Agrupa por nível e posiciona: cada linha centralizada em torno de x=0.
    const byLevel = new Map<number, string[]>();
    for (const [id, lvl] of level) {
      if (!byLevel.has(lvl)) byLevel.set(lvl, []);
      byLevel.get(lvl)!.push(id);
    }

    const pos = new Map<string, { x: number; y: number }>();
    for (const [lvl, ids] of [...byLevel.entries()].sort((a, b) => a[0] - b[0])) {
      const count = ids.length;
      const totalWidth = (count - 1) * H_GAP;
      ids.forEach((id, i) => {
        pos.set(id, { x: Math.round(i * H_GAP - totalWidth / 2), y: lvl * V_GAP });
      });
    }

    this.nodes.update((ns) => ns.map((n) => ({ ...n, ...(pos.get(n.id) ?? { x: n.x, y: n.y }) })));
    this.centerGraph();
    this.snack.open('Grafo reorganizado. Clique em Salvar para manter.', 'ok', { duration: 3500 });
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

  /** Abre o modal para criar uma nova variável. */
  protected addFormula(): void {
    this.openVariableDialog('create');
  }

  /** Abre o modal para editar a variável do índice informado. */
  protected editFormula(i: number): void {
    this.openVariableDialog('edit', i);
  }

  /**
   * Modal de variável: nome (linha) + conteúdo/fórmula (textarea maior). Ao
   * confirmar, cria ou atualiza a variável na lista. O rótulo acompanha a chave.
   */
  private openVariableDialog(mode: 'create' | 'edit', i?: number): void {
    const current = i !== undefined ? this.formulas()[i] : undefined;
    const data: VariableDialogData = {
      mode,
      key: current?.key ?? '',
      expression: current?.expression ?? '',
      readOnly: this.readOnly,
      fields: this.fieldNames(),
      // No autocomplete de {variavel}, não sugere a própria variável em edição.
      variables: this.variableNames().filter((v) => v !== current?.key),
      sources: this.sources(),
      policies: this.policies(),
    };
    const ref = this.dialog.open<VariableDialog, VariableDialogData, VariableResult>(VariableDialog, {
      data,
      // A largura/altura são controladas pelo container .resizable (arrastável e
      // redimensionável); o painel só não deve limitar o tamanho.
      maxWidth: '92vw',
      panelClass: 'resizable-dialog',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      if (result.deleted) {
        if (i !== undefined) this.formulas.update((fs) => fs.filter((_, j) => j !== i));
        return;
      }
      const entry: GraphFormula = { key: result.key, label: result.key, expression: result.expression };
      if (mode === 'create') {
        this.formulas.update((fs) => [...fs, entry]);
      } else if (i !== undefined) {
        this.formulas.update((fs) => fs.map((f, j) => (j === i ? entry : f)));
      }
    });
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
  protected removeRuleset(i: number): void {
    this.rulesets.update((rs) => rs.filter((_, j) => j !== i));
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

  /** Abre o modal para criar um novo campo. */
  protected addField(): void {
    this.openFieldDialog('create');
  }

  /** Abre o modal para editar o campo do índice informado. */
  protected editField(i: number): void {
    this.openFieldDialog('edit', i);
  }

  /** Modal de campo (nome técnico, rótulo, tipo, obrigatório). */
  private openFieldDialog(mode: 'create' | 'edit', i?: number): void {
    const current = i !== undefined ? this.inputFields()[i] : undefined;
    const data: FieldDialogData = {
      mode,
      name: current?.name ?? '',
      label: current?.label ?? '',
      type: current?.type ?? 'Number',
      required: current?.required ?? false,
      readOnly: this.readOnly,
    };
    const ref = this.dialog.open<FieldDialog, FieldDialogData, FieldResult>(FieldDialog, {
      data,
      width: '720px',
      maxWidth: '92vw',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      if (result.deleted) {
        if (i !== undefined) this.inputFields.update((fs) => fs.filter((_, j) => j !== i));
        return;
      }
      const { name, label, type, required } = result;
      if (mode === 'create') {
        this.inputFields.update((fs) => [...fs, { name, label, type, required, order: fs.length + 1 }]);
      } else if (i !== undefined) {
        this.inputFields.update((fs) => fs.map((f, j) => (j === i ? { ...f, name, label, type, required } : f)));
      }
    });
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

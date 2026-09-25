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
  FlowSummary,
  FlowVersionStatus,
  GraphEdge,
  GraphRule,
  GraphRuleset,
  VersionGraph,
  GraphFormula,
  GraphInputField,
  GraphTable,
  InputFieldType,
  InputSchemaField,
  RuleEffect,
  SourceDescriptorDto,
  ValidationWarning,
} from '../api/models';
import { apiErrorMessage } from '../shared/format';
import { defaultConfig, OUTCOME_LABEL, RULE_PALETTE } from './node-config';
import { FUNCTIONS } from './function-catalog';
import { NodeConfigDialog, NodeConfigData, NodeConfigResult } from './node-config-dialog';
import { BranchDialog, BranchData, BranchTarget } from './branch-dialog';
import { DecisionRunnerDialog, DecisionRunnerData } from './decision-runner-dialog';
import { VariableDialog, VariableDialogData, VariableResult } from './variable-dialog';
import { FieldDialog, FieldDialogData, FieldResult } from './field-dialog';
import { RequestSchemaDialog, RequestSchemaData, RequestSchemaResult } from './request-schema-dialog';
import { TableDialog, TableDialogData, TableResult } from './table-dialog';
import { FormulaInput } from './formula-input';
import {
  RenameKind,
  rewriteFormulas,
  rewriteNodeConfig,
  rewriteRulesets,
} from './reference-rename';

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
    FormulaInput,
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
  /** Avisos de validação do último save (fórmulas inválidas, PROCV sem chave, …). */
  protected readonly warnings = signal<ValidationWarning[]>([]);
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
  /**
   * Campos do schema DERIVADOS das fontes (origem Source): obrigatórios, exibidos
   * travados (não editáveis/excluíveis) e recalculados pelo backend a partir das
   * fontes usadas na política e nas referenciadas. Não são persistidos.
   */
  protected readonly sourceFields = signal<InputSchemaField[]>([]);
  protected readonly formulas = signal<GraphFormula[]>([]);
  /** Tabelas de parâmetros locais desta versão (lookup para PROCV/PROCV.FAIXA). */
  protected readonly tables = signal<GraphTable[]>([]);
  /** Tabelas globais (carregadas da API) — para o autocomplete de PROCV. */
  protected readonly globalTables = signal<{ name: string; columns: string[] }[]>([]);

  /**
   * Tabelas disponíveis para o autocomplete de PROCV: locais (desta versão) +
   * globais, com o nome e as colunas. Local de mesmo nome tem prioridade.
   */
  protected tableSpecs(): { name: string; columns: string[] }[] {
    const local = this.tables().map((t) => ({ name: t.name, columns: t.columns.map((c) => c.name) }));
    const localNames = new Set(local.map((t) => t.name.toLowerCase()));
    const globals = this.globalTables().filter((g) => !localNames.has(g.name.toLowerCase()));
    return [...local, ...globals];
  }
  protected readonly sources = signal<SourceDescriptorDto[]>([]);
  protected readonly policyName = signal<string>('Política atual');
  /** Nomes de todas as políticas (para o autocomplete de referência com '$['). */
  protected readonly policies = signal<string[]>([]);
  /**
   * Variáveis de cada política publicada, por nome da política. Alimenta o 3º
   * nível do autocomplete de referência cruzada — (Política;Variaveis;___) — para
   * sugerir as variáveis da política ALVO, não as locais desta versão.
   */
  protected readonly policyVariables = signal<Record<string, string[]>>({});

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
   *
   * O 4º argumento (`maxScale = 1`) limita a ampliação: sem ele, uma política
   * com poucos nós é escalada para preencher a viewport e os blocos abrem
   * gigantes. Com o teto em 1x, o grafo abre no tamanho natural (ou menor, se
   * for grande demais) e sempre centralizado. Assinatura confirmada no bundle:
   * fitToScreen(padding, animated, emitCanvasChange, maxScale).
   */
  private centerGraph(): void {
    setTimeout(() => this.canvas()?.fitToScreen({ x: 80, y: 80 } as IPoint, false, true, 1), 0);
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

    this.loadInputSchema();

    // Catálogo de fontes externas para o autocomplete ([Fonte;Produto;Dado]).
    this.api.listSources().subscribe({
      next: (list) => this.sources.set(list),
      error: () => this.sources.set([]),
    });

    // Tabelas globais para o autocomplete de PROCV (nomes + colunas).
    this.api.listGlobalTables().subscribe({
      next: (list) => this.globalTables.set(list.map((t) => ({ name: t.name, columns: t.columns.map((c) => c.name) }))),
      error: () => this.globalTables.set([]),
    });

    // Todas as políticas, para o autocomplete de referência cruzada com '('.
    // Além dos nomes, carregamos as variáveis de cada política PUBLICADA (o motor
    // só resolve referências contra a versão publicada), para o 3º nível do
    // autocomplete sugerir as variáveis da política alvo.
    this.api.listFlows().subscribe({
      next: (list) => {
        this.policies.set(list.map((f) => f.name).filter((n) => !!n));
        this.loadPolicyVariables(list);
      },
      error: () => this.policies.set([]),
    });
  }

  /**
   * Para cada política (exceto a atual), busca o grafo da sua versão MAIS RECENTE
   * (maior versionNumber, independente do status) e extrai as variáveis
   * (formulas). Monta o mapa nome→variáveis usado no 3º nível do autocomplete de
   * referência cruzada $[Política;Variaveis;___]. Usa a versão mais recente
   * (não só a publicada) porque as subpolíticas não precisam estar publicadas —
   * elas são congeladas na publicação da política principal. Falhas individuais
   * são ignoradas (uma política sem grafo não quebra as demais).
   */
  private loadPolicyVariables(flows: FlowSummary[]): void {
    for (const flow of flows) {
      if (flow.id === this.flowId) continue;
      const latest = [...flow.versions].sort((a, b) => b.versionNumber - a.versionNumber)[0];
      if (!latest) continue;
      this.api.getVersionGraph(flow.id, latest.id).subscribe({
        next: (graph) => {
          const vars = (graph.formulas ?? []).map((f) => f.key).filter((k) => !!k);
          this.policyVariables.update((m) => ({ ...m, [flow.name]: vars }));
        },
        error: () => {
          /* política sem grafo acessível: sem variáveis para sugerir */
        },
      });
    }
  }

  /** Nomes das variáveis locais (para o autocomplete de {variavel}). */
  protected variableNames(): string[] {
    return this.formulas().map((f) => f.key).filter((k) => !!k);
  }
  /** Nomes técnicos dos campos (para o autocomplete de 'campo'). */
  protected fieldNames(): string[] {
    return this.inputFields().map((f) => f.name).filter((n) => !!n);
  }

  /**
   * Carrega o schema de entrada e separa os campos derivados de fonte (origem
   * Source) para exibi-los travados. Chamado ao abrir e após salvar (as fontes
   * usadas podem ter mudado com as edições).
   */
  private loadInputSchema(): void {
    this.api.getInputSchema(this.flowId, this.versionId).subscribe({
      next: (schema) => this.sourceFields.set(schema.fields.filter((f) => f.origin === 'Source')),
      error: () => this.sourceFields.set([]),
    });
  }

  private applyGraph(graph: VersionGraph): void {
    this.rulesets.set(graph.rulesets);
    this.formulas.set(graph.formulas);
    this.inputFields.set(graph.inputFields ?? []);
    this.tables.set(graph.tables ?? []);

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

    const V_GAP = 150; // distância vertical entre níveis (centro a centro)
    const H_GAP = 40; // folga horizontal entre bordas de nós irmãos
    const DEFAULT_W = 210; // largura usada quando o nó ainda não foi medido

    // Largura REAL de cada nó, medida no DOM. Como os nós crescem conforme o
    // conteúdo, o layout precisa da largura verdadeira para centralizar os pais
    // sobre os filhos e nunca sobrepor irmãos de tamanhos diferentes.
    const widthOf = (id: string): number => {
      const el = document.querySelector<HTMLElement>(`[data-node-id="${id}"]`);
      return el?.offsetWidth || DEFAULT_W;
    };

    // Adjacência a partir das arestas (na ordem: true antes de false), sem
    // duplicar filhos e evitando revisitar (o grafo pode ter ciclos/reconvergência).
    const children = new Map<string, string[]>();
    for (const n of nodes) children.set(n.id, []);
    const ordered = [...this.edges()].sort((a, b) => {
      const rank = (h: string | null) => (h === 'true' ? 0 : h === 'false' ? 1 : 0);
      return rank(a.sourceHandle) - rank(b.sourceHandle);
    });
    const linked = new Set<string>();
    for (const e of ordered) {
      if (children.has(e.source) && children.has(e.target) && !linked.has(e.target)) {
        children.get(e.source)!.push(e.target);
        linked.add(e.target); // cada nó entra na árvore uma única vez (como filho)
      }
    }

    // Raiz: o Start; se não houver, o primeiro nó.
    const start = nodes.find((n) => n.kind === 'Start') ?? nodes[0];

    // Profundidade (nível/linha) de cada nó por BFS a partir da raiz.
    const depth = new Map<string, number>();
    const bfs: string[] = [start.id];
    depth.set(start.id, 0);
    while (bfs.length) {
      const id = bfs.shift()!;
      for (const c of children.get(id) ?? []) {
        if (!depth.has(c)) {
          depth.set(c, (depth.get(id) ?? 0) + 1);
          bfs.push(c);
        }
      }
    }

    // Layout de árvore centralizado (pós-ordem): o centro X de cada nó é a média
    // dos centros dos filhos; folhas são empacotadas lado a lado respeitando a
    // largura real de cada uma. `cursor` acompanha o próximo X livre por subárvore.
    const centerX = new Map<string, number>();
    let cursor = 0;
    const visited = new Set<string>();

    const place = (id: string): number => {
      if (visited.has(id)) return centerX.get(id) ?? cursor;
      visited.add(id);

      const kids = (children.get(id) ?? []).filter((c) => !visited.has(c));
      const halfSelf = widthOf(id) / 2;

      if (kids.length === 0) {
        // Folha: ocupa o próximo espaço livre, avançando pela sua largura.
        const cx = cursor + halfSelf;
        cursor += widthOf(id) + H_GAP;
        centerX.set(id, cx);
        return cx;
      }

      // Posiciona os filhos primeiro; o pai fica centralizado sobre eles.
      const kidCenters = kids.map((k) => place(k));
      const cx = (kidCenters[0] + kidCenters[kidCenters.length - 1]) / 2;
      centerX.set(id, cx);
      return cx;
    };

    place(start.id);

    // Nós não alcançados a partir da raiz (soltos): empacota numa linha ao final.
    const maxDepth = Math.max(0, ...[...depth.values()]);
    for (const n of nodes) {
      if (!visited.has(n.id)) {
        depth.set(n.id, maxDepth + 1);
        const cx = cursor + widthOf(n.id) / 2;
        cursor += widthOf(n.id) + H_GAP;
        centerX.set(n.id, cx);
        visited.add(n.id);
      }
    }

    // Converte centro X -> canto superior-esquerdo (o que o fNodePosition espera),
    // subtraindo metade da largura real de cada nó. Y pela profundidade.
    const pos = new Map<string, { x: number; y: number }>();
    for (const n of nodes) {
      const cx = centerX.get(n.id) ?? 0;
      const lvl = depth.get(n.id) ?? 0;
      pos.set(n.id, { x: Math.round(cx - widthOf(n.id) / 2), y: lvl * V_GAP });
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
      // Mesmas listas do editor de variáveis, para o autocomplete de fórmula
      // (', {, [, () funcionar nos campos de condição, cálculo, ações e matriz.
      fields: this.fieldNames(),
      variables: this.variableNames(),
      sources: this.sources(),
      policies: this.policies(),
      policyVariables: this.policyVariables(),
      tables: this.tableSpecs(),
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

  /**
   * Propaga um rename por TODO o grafo: reescreve as referências ao nome antigo
   * (variável `{x}`, campo `'x'`, tabela no 1º arg de PROCV) em todas as
   * expressões — variáveis, condições de regra e configs dos nós. Chamado ao
   * editar (renomear) uma variável, campo ou tabela, antes de aplicar a troca do
   * próprio item. Sem efeito quando o nome não mudou.
   */
  private renameReference(kind: RenameKind, oldName: string, newName: string): void {
    if (!oldName || oldName === newName) return;

    this.formulas.update((fs) => rewriteFormulas(fs, kind, oldName, newName));
    this.rulesets.update((rs) => rewriteRulesets(rs, kind, oldName, newName));
    this.nodes.update((ns) =>
      ns.map((n) => ({
        ...n,
        config: rewriteNodeConfig(n.config, n.kind, kind, oldName, newName),
      })),
    );
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
    return {
      nodes,
      edges,
      rulesets: this.rulesets(),
      formulas: this.formulas(),
      inputFields: this.inputFields(),
      tables: this.tables(),
    };
  }

  // --- Editor lateral: Tabelas de parâmetros ---------------------------

  /** Tabela vazia base para o modal de criação. */
  private emptyTable(): GraphTable {
    return { name: '', label: '', columns: [], rows: [], keyColumn: null, minColumn: null, maxColumn: null, defaultValue: null };
  }

  protected addTable(): void {
    this.openTableDialog('create');
  }

  protected editTable(i: number): void {
    this.openTableDialog('edit', i);
  }

  /**
   * Modal de tabela. Escopo local grava no signal desta versão (salvo com o
   * grafo). Escopo global grava via API (/global-tables) na hora, pois vive fora
   * da versão. Na criação o usuário escolhe o escopo; na edição de uma local, ela
   * permanece local (globais são editadas na tela dedicada).
   */
  private openTableDialog(mode: 'create' | 'edit', i?: number): void {
    const current = i !== undefined ? this.tables()[i] : undefined;
    const data: TableDialogData = {
      mode,
      allowScope: mode === 'create',
      scope: 'local',
      table: current ? { ...current } : this.emptyTable(),
      readOnly: this.readOnly,
    };
    const ref = this.dialog.open<TableDialog, TableDialogData, TableResult>(TableDialog, {
      data,
      maxWidth: '94vw',
      panelClass: 'resizable-dialog',
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;

      if (result.scope === 'global') {
        // Tabela global: persiste imediatamente via API (não entra no grafo local).
        this.api.createGlobalTable({
          name: result.table.name,
          label: result.table.label,
          columns: result.table.columns,
          rows: result.table.rows,
          keyColumn: result.table.keyColumn,
          minColumn: result.table.minColumn,
          maxColumn: result.table.maxColumn,
          defaultValue: result.table.defaultValue,
        }).subscribe({
          next: () => this.snack.open('Tabela global criada.', 'ok', { duration: 2500 }),
          error: (e) => this.snack.open(apiErrorMessage(e, 'Falha ao criar tabela global.'), 'ok', { duration: 4000 }),
        });
        return;
      }

      // Tabela local: opera no signal (salva junto com o grafo).
      if (result.deleted) {
        if (i !== undefined) this.tables.update((ts) => ts.filter((_, j) => j !== i));
        return;
      }
      if (mode === 'create') {
        this.tables.update((ts) => [...ts, result.table]);
      } else if (i !== undefined) {
        // Renomeou a tabela? Propaga o novo nome para o 1º arg dos PROCV.
        if (current && current.name !== result.table.name) {
          this.renameReference('table', current.name, result.table.name);
        }
        this.tables.update((ts) => ts.map((t, j) => (j === i ? result.table : t)));
      }
    });
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
      policyVariables: this.policyVariables(),
      tables: this.tableSpecs(),
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
        // Renomeou a variável? Propaga o novo nome para {x} em todas as fórmulas.
        if (current && current.key !== result.key) {
          this.renameReference('variable', current.key, result.key);
        }
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
      description: current?.description ?? '',
      example: current?.example ?? '',
      group: current?.group ?? '',
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
      const { name, label, type, required, description, example, group } = result;
      if (mode === 'create') {
        this.inputFields.update((fs) => [...fs, { name, label, type, required, description, example, group, order: fs.length + 1 }]);
      } else if (i !== undefined) {
        // Renomeou o campo? Propaga o novo nome para 'x' em todas as fórmulas.
        if (current && current.name !== name) {
          this.renameReference('field', current.name, name);
        }
        this.inputFields.update((fs) => fs.map((f, j) => (j === i ? { ...f, name, label, type, required, description, example, group } : f)));
      }
    });
  }

  protected removeField(i: number): void {
    this.inputFields.update((fs) => fs.filter((_, j) => j !== i));
  }

  protected save(): void {
    this.error.set(null);
    this.api.saveVersionGraph(this.flowId, this.versionId, this.currentGraph()).subscribe({
      next: (graph) => this.handleSaveWarnings(graph.warnings ?? []),
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao salvar.')),
    });
  }

  /**
   * Mostra o resultado do save. Salvou sempre (não bloqueia), mas se a validação
   * apontou problemas nas fórmulas/tabelas, lista os avisos num painel; senão,
   * confirma com um snackbar simples.
   */
  private handleSaveWarnings(warnings: ValidationWarning[]): void {
    // As fontes usadas podem ter mudado — recalcula os campos derivados.
    this.loadInputSchema();
    this.warnings.set(warnings);
    if (warnings.length === 0) {
      this.snack.open('Rascunho salvo.', 'ok', { duration: 2500 });
      return;
    }
    const errs = warnings.filter((w) => w.severity === 'Error').length;
    const msg = errs > 0
      ? `Salvo com ${errs} erro(s) de validação.`
      : `Salvo com ${warnings.length} aviso(s).`;
    this.snack.open(msg, 'ok', { duration: 3500 });
  }

  protected dismissWarnings(): void {
    this.warnings.set([]);
  }

  protected publish(): void {
    // Publicar é irreversível na prática: valida e publica esta versão, arquiva
    // a anterior e invalida o cache dos fluxos publicados. Confirma antes.
    const ok = window.confirm(
      `Publicar esta versão da política "${this.policyName()}"?\n\n` +
        'A versão publicada atual será arquivada e esta passará a ser a vigente ' +
        'para as decisões. Após publicar, a versão fica somente leitura.',
    );
    if (!ok) return;

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
      error: (e) => this.error.set(apiErrorMessage(e, 'Falha ao criar nova versão.')),
    });
  }

  /**
   * Abre a definição da REQUEST oficial: edita os campos manuais (que vivem no
   * mesmo signal inputFields, salvos com o grafo), mostra os campos derivados de
   * fonte (travados) e o exemplo do payload. Ao aplicar, atualiza os campos
   * manuais; o usuário salva normalmente para persistir.
   */
  protected openRequestSchema(): void {
    const ref = this.dialog.open<RequestSchemaDialog, RequestSchemaData, RequestSchemaResult>(
      RequestSchemaDialog,
      {
        data: {
          flowId: this.flowId,
          versionId: this.versionId,
          fields: this.inputFields(),
          readOnly: this.readOnly,
        },
        width: '860px',
        maxWidth: '92vw',
      },
    );
    ref.afterClosed().subscribe((result) => {
      if (!result || this.readOnly) return;
      this.inputFields.set(result.fields);
    });
  }

  protected openTest(): void {
    // Rascunho: testa a versão atual (sem publicar). Publicada/arquivada: roda a
    // versão publicada pelo caminho normal.
    const isDraft = this.status() === 'Draft';
    const data: DecisionRunnerData = {
      flowId: this.flowId,
      inputFields: this.inputFields(),
      versionId: this.versionId,
      test: isDraft,
    };
    this.dialog.open<DecisionRunnerDialog, DecisionRunnerData>(DecisionRunnerDialog, { data, width: '720px' });
  }
}

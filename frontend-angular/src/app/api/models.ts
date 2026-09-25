// Modelos espelhando os contratos do backend .NET (MotorDecisao.*). Os valores
// dos enums batem com os nomes dos enums C#, que a API serializa/aceita como
// string. Portado do front React (frontend/src/api/types.ts).

export type FlowNodeKind =
  | 'Start'
  | 'Condition'
  | 'Ruleset'
  | 'Computation'
  | 'DataSource'
  | 'Decision'
  | 'Action'
  | 'Matrix'
  | 'Comment';

export type FlowVersionStatus = 'Draft' | 'Published' | 'Archived';

export type RuleEffect = 'Score' | 'Decision' | 'Annotation';

export type DecisionOutcome =
  | 'Pending'
  | 'Approved'
  | 'ApprovedWithConditions'
  | 'ManualReview'
  | 'Denied';

export type ExecutionStatus = 'Running' | 'Completed' | 'Failed';

export interface FlowVersionSummary {
  id: string;
  versionNumber: number;
  status: FlowVersionStatus;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  /**
   * Políticas (bundles publicados ativos) de OUTRAS políticas que congelam esta
   * versão como membro — o "Vinculado a" do histórico. Vazio quando nenhuma.
   */
  linkedPolicies: string[];
}

export interface FlowSummary {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  versions: FlowVersionSummary[];
}

export interface GraphNode {
  nodeKey: string;
  kind: FlowNodeKind;
  label: string;
  positionX: number;
  positionY: number;
  config: string;
  rulesetKey: string | null;
}

export interface GraphEdge {
  edgeKey: string;
  sourceNodeKey: string;
  targetNodeKey: string;
  sourceHandle: string | null;
  label: string | null;
}

export interface GraphRule {
  order: number;
  name: string;
  conditionExpression: string;
  effect: RuleEffect;
  scoreWeight: number;
  forcedOutcome: DecisionOutcome | null;
  message: string | null;
  isEnabled: boolean;
}

export interface GraphRuleset {
  rulesetKey: string;
  name: string;
  description: string | null;
  approvalThreshold: number | null;
  rules: GraphRule[];
}

export interface GraphFormula {
  key: string;
  label: string;
  expression: string;
}

export type InputFieldType = 'Number' | 'Text' | 'Boolean' | 'Date';

export interface GraphInputField {
  name: string;
  label: string;
  type: InputFieldType;
  required: boolean;
  order: number;
  /** Descrição do campo (documenta a request). */
  description?: string | null;
  /** Valor de exemplo (usado no payload de exemplo). */
  example?: string | null;
  /** Grupo/assunto do campo (ex.: proponente, operacao). Vazio = raiz. */
  group?: string | null;
}

/** Origem de um campo do schema de entrada. */
export type InputFieldOrigin = 'Manual' | 'Source';

/** Um campo do schema de entrada (request) da política. */
export interface InputSchemaField {
  name: string;
  label: string;
  type: InputFieldType;
  required: boolean;
  origin: InputFieldOrigin;
  /** Fontes que exigem este campo como chave (ex.: ["SERASA/Score"]). Vazio se manual. */
  requiredBySources: string[];
  description?: string | null;
  example?: string | null;
  group?: string | null;
}

/** Schema de entrada de uma versão: contrato da request de decisão. */
export interface PolicyInputSchema {
  flowId: string;
  flowVersionId: string;
  fields: InputSchemaField[];
  /** Exemplo do payload do POST /decisions, gerado a partir dos campos. */
  exampleRequestJson: string;
}

/** Tipo de coluna de uma tabela de parâmetros. */
export type ParameterColumnType = 'Number' | 'Text' | 'Boolean' | 'Date';

/** Coluna de uma tabela de parâmetros: nome técnico + tipo. */
export interface GraphTableColumn {
  name: string;
  type: ParameterColumnType;
}

/**
 * Tabela de parâmetros (lookup) LOCAL da versão — viaja dentro do VersionGraph,
 * salva/carregada junto com o grafo. `rows` é uma matriz de strings [linha][coluna]
 * na ordem de `columns`. keyColumn (busca exata PROCV), minColumn/maxColumn (busca
 * por faixa PROCV.FAIXA) e defaultValue (retorno quando não casa).
 */
export interface GraphTable {
  name: string;
  label: string;
  columns: GraphTableColumn[];
  rows: string[][];
  keyColumn: string | null;
  minColumn: string | null;
  maxColumn: string | null;
  defaultValue: string | null;
}

export interface VersionGraph {
  nodes: GraphNode[];
  edges: GraphEdge[];
  rulesets: GraphRuleset[];
  formulas: GraphFormula[];
  inputFields: GraphInputField[];
  tables: GraphTable[];
  /** Avisos de validação preenchidos na resposta do save (vazio no GET). */
  warnings?: ValidationWarning[];
}

export type ValidationSeverity = 'Error' | 'Warning';

/** Aviso de validação do grafo: gravidade, local amigável e mensagem pt-BR. */
export interface ValidationWarning {
  severity: ValidationSeverity;
  where: string;
  message: string;
}

/** Tabela de parâmetros GLOBAL (compartilhada entre políticas). */
export interface GlobalParameterTable {
  id: string;
  name: string;
  label: string;
  columns: GraphTableColumn[];
  rows: string[][];
  keyColumn: string | null;
  minColumn: string | null;
  maxColumn: string | null;
  defaultValue: string | null;
  createdAt: string;
  updatedAt: string | null;
}

/** Corpo para criar/atualizar uma tabela global. */
export interface GlobalParameterTableInput {
  name: string;
  label: string;
  columns: GraphTableColumn[];
  rows: string[][];
  keyColumn: string | null;
  minColumn: string | null;
  maxColumn: string | null;
  defaultValue: string | null;
}

/** Catálogo read-only de fontes externas (GET /sources). */
export interface SourceDatumDto {
  name: string;
  description: string | null;
}
export interface SourceProductDto {
  name: string;
  description: string | null;
  /** Campo obrigatório da proposta usado como chave da consulta/cache (ex.: cpf, cnpj, placa). */
  keyField: string;
  data: SourceDatumDto[];
}
/** Parâmetros operacionais de uma fonte (tela de Fontes). */
export interface SourceConfigDto {
  maxAttempts: number;
  timeoutSeconds: number;
  cacheTtlHours: number;
}

export interface SourceDescriptorDto {
  name: string;
  description: string | null;
  products: SourceProductDto[];
}

/** Um nó na árvore de resolução (passo a passo) de uma fórmula. */
export interface EvalStep {
  depth: number;
  expression: string;
  value: string;
}

/** Bloco/categoria a que um passo da trilha pertence (para agrupar o log). */
export type TraceCategory =
  | 'Fonte'
  | 'Variavel'
  | 'Inicio'
  | 'Fluxo'
  | 'Regra'
  | 'Acao'
  | 'Matriz'
  | 'Decisao';

export interface TraceStep {
  sequence: number;
  nodeKey: string;
  nodeLabel: string;
  expression: string | null;
  result: string | null;
  message: string | null;
  category: TraceCategory;
  /** Política a que o passo pertence (principal ou subpolítica). Null em traces antigos. */
  policyName: string | null;
  /** Para passos de fonte: origem do valor ('Online' | 'Cache'). Null nos demais. */
  sourceOrigin?: string | null;
  detail: EvalStep[];
}

export interface DecisionResponse {
  executionId: string | null;
  flowId: string;
  flowVersionId: string;
  outcome: DecisionOutcome;
  score: number;
  limit: number;
  justifications: string[];
  outputs: Record<string, string>;
  status: ExecutionStatus;
  error: string | null;
  trace: TraceStep[];
}

export type ActionType =
  | 'AddPoints'
  | 'SetPoints'
  | 'AddLimit'
  | 'SetLimit'
  | 'AddJustification'
  | 'SetJustification'
  | 'SetOutput';

export interface ActionItem {
  type: ActionType;
  expression: string;
  name?: string | null;
}
export interface ActionsConfig {
  actions: ActionItem[];
}

export type MatrixMode = 'Points' | 'Limit' | 'Decision';
export interface MatrixBand {
  label: string;
  min: number | null;
  max: number | null;
}
export interface MatrixConfig {
  mode: MatrixMode;
  rowExpression: string;
  colExpression: string;
  rowBands: MatrixBand[];
  colBands: MatrixBand[];
  cells: string[][];
  defaultValue?: string | null;
}

export interface ExecutionSummary {
  id: string;
  proposalReference: string | null;
  outcome: DecisionOutcome;
  score: number;
  status: ExecutionStatus;
  createdAt: string;
}

export interface ExecutionDetail extends ExecutionSummary {
  flowVersionId: string;
  inputData: string;
  limit: number;
  justifications: string[];
  outputs: Record<string, string>;
  error: string | null;
  completedAt: string | null;
  trace: TraceStep[];
}

/** Payloads de config por tipo de nó (serializados em GraphNode.config como JSON). */
export interface ConditionConfig {
  expression: string;
}
export interface ComputationAssignment {
  targetField: string;
  expression: string;
}
export interface ComputationConfig {
  assignments: ComputationAssignment[];
}
export interface DecisionConfig {
  outcome: DecisionOutcome;
  message?: string | null;
}
export interface DataSourceConfig {
  source: string;
  parameters?: Record<string, string> | null;
}
export interface CommentConfig {
  text: string;
}

/** Variável global (reutilizável entre políticas). */
export interface GlobalVariable {
  id: string;
  key: string;
  label: string;
  expression: string;
  createdAt: string;
  updatedAt: string | null;
}

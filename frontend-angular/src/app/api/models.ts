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
}

export interface VersionGraph {
  nodes: GraphNode[];
  edges: GraphEdge[];
  rulesets: GraphRuleset[];
  formulas: GraphFormula[];
  inputFields: GraphInputField[];
}

/** Catálogo read-only de fontes externas (GET /sources). */
export interface SourceDatumDto {
  name: string;
  description: string | null;
}
export interface SourceProductDto {
  name: string;
  description: string | null;
  data: SourceDatumDto[];
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

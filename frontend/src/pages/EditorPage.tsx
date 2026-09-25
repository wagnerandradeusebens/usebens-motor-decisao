import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import ReactFlow, {
  addEdge,
  Background,
  Controls,
  MiniMap,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
  type Node,
  type ReactFlowInstance,
} from 'reactflow';
import 'reactflow/dist/style.css';
import { api, ApiError } from '../api/client';
import type {
  DecisionOutcome,
  FlowNodeKind,
  GlobalVariable,
  GraphRuleset,
  SourceDescriptorDto,
  VersionGraph,
} from '../api/types';
import { nodeTypes, type MotorNodeData } from '../editor/nodeTypes';
import { toReactFlow, toVersionGraph } from '../editor/graphMapping';
import { NodeInspector } from '../editor/NodeInspector';
import { RulesetEditor } from '../editor/RulesetEditor';
import { DecisionRunner } from '../editor/DecisionRunner';
import { VariablesPanel, Modal } from '../editor/VariablesPanel';
import { GlobalVariablesPanel } from '../editor/GlobalVariablesPanel';
import { FieldsPanel } from '../editor/FieldsPanel';
import { FunctionsPanel, AnalysisObjectPanel } from '../editor/FunctionsPanel';
import { BranchModal, type BranchTarget } from '../editor/BranchModal';
import { Accordion } from '../editor/Accordion';
import { ComingSoonPanel, SourcesRefPanel, OperatorsPanel } from '../editor/MenuPanels';

// Draggable palette items, grouped under "Regra" in the Crivo-style menu.
const RULE_PALETTE: { kind: FlowNodeKind; label: string }[] = [
  { kind: 'Condition', label: 'Nova Regra (condição)' },
  { kind: 'Matrix', label: 'Regra Matriz' },
  { kind: 'Ruleset', label: 'Conjunto de regras' },
  { kind: 'Computation', label: 'Cálculo' },
  { kind: 'DataSource', label: 'Fonte de dados' },
  { kind: 'Action', label: 'Ação' },
  { kind: 'Decision', label: 'Decisão' },
  { kind: 'Comment', label: 'Comentário' },
];

type ConfigMap = Record<string, { config: string; rulesetKey: string | null }>;

function defaultConfig(kind: FlowNodeKind): string {
  switch (kind) {
    case 'Condition':
      return JSON.stringify({ expression: '' });
    case 'Computation':
      return JSON.stringify({ assignments: [] });
    case 'Decision':
      return JSON.stringify({ outcome: 'Approved', message: '' });
    case 'DataSource':
      return JSON.stringify({ source: '' });
    case 'Action':
      return JSON.stringify({ actions: [] });
    case 'Matrix':
      return JSON.stringify({
        mode: 'Points', rowExpression: '', colExpression: '',
        rowBands: [], colBands: [], cells: [], defaultValue: '0',
      });
    case 'Comment':
      return JSON.stringify({ text: '' });
    default:
      return '{}';
  }
}

const OUTCOME_LABEL: Record<DecisionOutcome, string> = {
  Pending: 'Pendente',
  Approved: 'Aprovar',
  ApprovedWithConditions: 'Aprovar c/ condições',
  ManualReview: 'Revisão manual',
  Denied: 'Negar',
};

function EditorInner() {
  const { flowId, versionId } = useParams();
  const navigate = useNavigate();
  const [nodes, setNodes, onNodesChange] = useNodesState<MotorNodeData>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState([]);
  const [configByNode, setConfigByNode] = useState<ConfigMap>({});
  const [rulesets, setRulesets] = useState<GraphRuleset[]>([]);
  const [formulas, setFormulas] = useState<VersionGraph['formulas']>([]);
  const [inputFields, setInputFields] = useState<VersionGraph['inputFields']>([]);
  const [sources, setSources] = useState<SourceDescriptorDto[]>([]);
  const [globalVars, setGlobalVars] = useState<GlobalVariable[]>([]);
  const [status, setStatus] = useState<string>('Draft');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Modals
  const [editingNodeId, setEditingNodeId] = useState<string | null>(null);
  const [branchModal, setBranchModal] = useState<{ nodeKey: string; branch: 'true' | 'false' } | null>(null);
  const [testOpen, setTestOpen] = useState(false);

  const rfInstance = useRef<ReactFlowInstance | null>(null);
  const wrapper = useRef<HTMLDivElement | null>(null);
  const readOnly = status !== 'Draft';

  useEffect(() => {
    if (!flowId || !versionId) return;
    (async () => {
      try {
        const [flow, graph, srcs] = await Promise.all([
          api.getFlow(flowId),
          api.getVersionGraph(flowId, versionId),
          api.listSources().catch(() => []),
        ]);
        setStatus(flow.versions.find((v) => v.id === versionId)?.status ?? 'Draft');
        setSources(srcs);

        let { nodes: rfNodes, edges: rfEdges } = toReactFlow(graph);
        const cfg: ConfigMap = {};
        for (const n of graph.nodes) cfg[n.nodeKey] = { config: n.config, rulesetKey: n.rulesetKey };

        if (rfNodes.length === 0) {
          rfNodes = [{ id: 'start', type: 'motor', position: { x: 300, y: 40 }, data: { label: 'Início', kind: 'Start' } }];
          cfg['start'] = { config: '{}', rulesetKey: null };
        }

        // The Start node cannot be deleted (every policy needs exactly one).
        rfNodes = rfNodes.map((n) => (n.data.kind === 'Start' ? { ...n, deletable: false } : n));

        setNodes(rfNodes);
        setEdges(rfEdges);
        setRulesets(graph.rulesets);
        setFormulas(graph.formulas);
        setInputFields(graph.inputFields ?? []);
        setConfigByNode(cfg);
      } catch (e) {
        setError(e instanceof ApiError ? e.message : 'Falha ao carregar a versão.');
      }
    })();
  }, [flowId, versionId, setNodes, setEdges]);

  const onConnect = useCallback(
    (conn: Connection) =>
      setEdges((eds) => addEdge({ ...conn, id: `e-${Date.now()}`, label: conn.sourceHandle ?? undefined }, eds)),
    [setEdges],
  );

  const onDrop = useCallback(
    (event: React.DragEvent) => {
      event.preventDefault();
      const kind = event.dataTransfer.getData('application/motor-node') as FlowNodeKind;
      if (!kind || !rfInstance.current) return;
      const pos = rfInstance.current.screenToFlowPosition({ x: event.clientX, y: event.clientY });
      const id = `${kind.toLowerCase()}-${Date.now()}`;
      const node: Node<MotorNodeData> = { id, type: 'motor', position: pos, data: { label: kind, kind } };
      setNodes((nds) => nds.concat(node));
      setConfigByNode((m) => ({ ...m, [id]: { config: defaultConfig(kind), rulesetKey: null } }));
    },
    [setNodes],
  );

  const editingNode = useMemo(() => nodes.find((n) => n.id === editingNodeId) ?? null, [nodes, editingNodeId]);
  const fieldNames = useMemo(() => inputFields.map((f) => f.name).filter(Boolean), [inputFields]);
  const variableNames = useMemo(() => {
    // Local variables plus globals not shadowed by a local of the same name.
    const localKeys = formulas.map((f) => f.key).filter(Boolean);
    const localSet = new Set(localKeys.map((k) => k.toLowerCase()));
    const globalKeys = globalVars.map((g) => g.key).filter((k) => k && !localSet.has(k.toLowerCase()));
    return [...localKeys, ...globalKeys];
  }, [formulas, globalVars]);

  function currentGraph(): VersionGraph {
    return toVersionGraph(nodes, edges, configByNode, rulesets, formulas, inputFields);
  }

  /** Removes a node, its edges, and its stored config. Start cannot be removed. */
  function deleteNode(id: string) {
    const node = nodes.find((n) => n.id === id);
    if (!node || node.data.kind === 'Start') return;
    setNodes((nds) => nds.filter((n) => n.id !== id));
    setEdges((eds) => eds.filter((e) => e.source !== id && e.target !== id));
    setConfigByNode((m) => {
      const next = { ...m };
      delete next[id];
      return next;
    });
    if (editingNodeId === id) setEditingNodeId(null);
  }

  function applyBranch(target: BranchTarget) {
    if (!branchModal) return;
    const { nodeKey, branch } = branchModal;

    let targetKey: string;
    if (target.kind === 'node') {
      targetKey = target.nodeKey;
    } else {
      targetKey = `decision-${Date.now()}`;
      const src = nodes.find((n) => n.id === nodeKey);
      const pos = { x: (src?.position.x ?? 0) + (branch === 'true' ? -120 : 160), y: (src?.position.y ?? 0) + 140 };
      setNodes((nds) =>
        nds.concat({ id: targetKey, type: 'motor', position: pos, data: { label: OUTCOME_LABEL[target.outcome], kind: 'Decision' } }),
      );
      setConfigByNode((m) => ({ ...m, [targetKey]: { config: JSON.stringify({ outcome: target.outcome }), rulesetKey: null } }));
    }

    setEdges((eds) => {
      const filtered = eds.filter((e) => !(e.source === nodeKey && (e.sourceHandle ?? undefined) === branch));
      const edge: Edge = {
        id: `e-${Date.now()}`,
        source: nodeKey,
        target: targetKey,
        sourceHandle: branch,
        label: branch === 'true' ? 'V' : 'F',
      };
      return filtered.concat(edge);
    });
    setBranchModal(null);
  }

  async function save() {
    if (!flowId || !versionId) return;
    setError(null);
    setMessage(null);
    try {
      await api.saveVersionGraph(flowId, versionId, currentGraph());
      setMessage('Rascunho salvo.');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao salvar.');
    }
  }

  async function publish() {
    if (!flowId || !versionId) return;
    setError(null);
    setMessage(null);
    try {
      await api.saveVersionGraph(flowId, versionId, currentGraph());
      const v = await api.publishVersion(flowId, versionId);
      setStatus(v.status);
      setMessage('Versão publicada.');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao publicar.');
    }
  }

  // Creates a new Draft copying the current version, and opens it for editing.
  async function createDraft() {
    if (!flowId || !versionId) return;
    setError(null);
    try {
      const v = await api.createVersion(flowId, versionId);
      navigate(`/flows/${flowId}/versions/${v.id}`);
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Falha ao criar rascunho.');
    }
  }

  return (
    <div className="editor">
      {/* Left sidebar: Crivo-style menu sections */}
      <div className="palette">
        <Accordion title="Regra" defaultOpen>
          <div className="hint" style={{ marginBottom: 8 }}>Arraste para o canvas.</div>
          {RULE_PALETTE.map((item) => (
            <div
              key={item.kind}
              className="palette-item"
              draggable={!readOnly}
              onDragStart={(e) => e.dataTransfer.setData('application/motor-node', item.kind)}
            >
              {item.label}
            </div>
          ))}
          <div className="palette-item" style={{ opacity: 0.55, cursor: 'not-allowed' }} title="Em breve">
            Campeão/Desafiante <span className="badge Draft" style={{ marginLeft: 4 }}>em breve</span>
          </div>
          <div className="hint" style={{ marginTop: 8 }}>
            Duplo-clique num nó para configurá-lo. Delete (ou "Excluir nó") para remover.
          </div>
        </Accordion>

        <Accordion title="Minhas Variáveis" defaultOpen>
          <VariablesPanel variables={formulas} fields={fieldNames} sources={sources} readOnly={readOnly} onChange={setFormulas} />
        </Accordion>
        <Accordion title="Variáveis Globais">
          <GlobalVariablesPanel fields={fieldNames} sources={sources} onChange={setGlobalVars} />
        </Accordion>
        <Accordion title="Fontes de Informação">
          <SourcesRefPanel sources={sources} />
        </Accordion>
        <Accordion title="Campos">
          <FieldsPanel fields={inputFields} readOnly={readOnly} onChange={setInputFields} />
        </Accordion>
        <Accordion title="Objetos de Análise">
          <ComingSoonPanel text="Encadear sub-análises (ex.: sócios de uma empresa)." />
        </Accordion>
        <Accordion title="Parâmetros de Saída">
          <ComingSoonPanel text="Valores nomeados devolvidos pela decisão (limite, taxa, justificativa)." />
        </Accordion>
        <Accordion title="Operadores">
          <OperatorsPanel />
        </Accordion>
        <Accordion title="Funções">
          <FunctionsPanel />
        </Accordion>
        <Accordion title="Ações">
          <div className="hint">
            Arraste um bloco <strong>Ação</strong> (seção Regra) e configure: adiciona/define
            pontos e limite, adiciona/define justificativa, define parâmetro de saída.
          </div>
        </Accordion>
        <Accordion title="Regras (scorecard)">
          <RulesetEditor rulesets={rulesets} readOnly={readOnly} onChange={setRulesets} />
        </Accordion>
        <Accordion title="Objeto de análise (config)">
          <AnalysisObjectPanel />
        </Accordion>

        <div style={{ marginTop: 16 }}>
          <Link to={`/flows/${flowId}`}>← voltar à política</Link>
        </div>
      </div>

      {/* Canvas */}
      <div className="canvas-wrap" ref={wrapper}>
        <div className="toolbar">
          <span className={`badge ${status}`}>{status}</span>
          <button className="btn btn--secondary btn--sm" onClick={() => void save()} disabled={readOnly}>Salvar</button>
          <button className="btn btn--primary btn--sm" onClick={() => void publish()} disabled={readOnly}>Publicar</button>
          <button className="btn btn--secondary btn--sm" onClick={() => setTestOpen(true)} disabled={status !== 'Published'}>Testar</button>
          <Link to={`/flows/${flowId}/execucoes`}><button className="btn btn--secondary btn--sm">Execuções</button></Link>
          {message && <span className="muted">{message}</span>}
          {error && <span style={{ color: 'var(--cor-negativo)' }}>{error}</span>}
        </div>
        {readOnly && (
          <div
            className="row spread"
            style={{
              background: 'rgba(255,193,7,0.15)', borderBottom: '1px solid var(--cor-alerta)',
              padding: '0.5rem 1rem', fontSize: '0.85rem',
            }}
          >
            <span>Esta versão está <strong>{status === 'Published' ? 'publicada' : 'arquivada'}</strong> e é somente leitura.</span>
            <button className="btn btn--primary btn--sm" onClick={() => void createDraft()}>
              Criar rascunho para editar
            </button>
          </div>
        )}
        <div style={{ height: 'calc(100% - 45px)' }} onDrop={onDrop} onDragOver={(e) => e.preventDefault()}>
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onInit={(inst) => (rfInstance.current = inst)}
            onNodeDoubleClick={(_, node) => setEditingNodeId(node.id)}
            onNodesDelete={(deleted) => {
              // Keep Start, and drop config for the removed nodes.
              setConfigByNode((m) => {
                const next = { ...m };
                for (const d of deleted) delete next[d.id];
                return next;
              });
            }}
            deleteKeyCode={readOnly ? null : ['Delete', 'Backspace']}
            fitView
          >
            <Background />
            <Controls />
            <MiniMap />
          </ReactFlow>
        </div>
      </div>

      {/* Node config modal (double-click) */}
      {editingNode && (
        <Modal title={`Configurar: ${editingNode.data.label}`} onClose={() => setEditingNodeId(null)}>
          <NodeInspector
            nodeKey={editingNode.id}
            kind={editingNode.data.kind}
            label={editingNode.data.label}
            config={configByNode[editingNode.id]?.config ?? '{}'}
            rulesetKey={configByNode[editingNode.id]?.rulesetKey ?? null}
            rulesets={rulesets}
            readOnly={readOnly}
            fields={fieldNames}
            variables={variableNames}
            sources={sources}
            onLabelChange={(label) =>
              setNodes((nds) => nds.map((n) => (n.id === editingNode.id ? { ...n, data: { ...n.data, label } } : n)))
            }
            onConfigChange={(config) =>
              setConfigByNode((m) => ({ ...m, [editingNode.id]: { config, rulesetKey: m[editingNode.id]?.rulesetKey ?? null } }))
            }
            onRulesetKeyChange={(rulesetKey) =>
              setConfigByNode((m) => ({ ...m, [editingNode.id]: { config: m[editingNode.id]?.config ?? '{}', rulesetKey } }))
            }
          />

          {editingNode.data.kind === 'Condition' && !readOnly && (
            <div className="card" style={{ padding: '0.7rem 0.8rem', marginTop: '0.8rem' }}>
              <div className="form-label">Saídas da condição</div>
              <div className="row" style={{ gap: 8 }}>
                <button className="btn btn--secondary btn--sm" onClick={() => setBranchModal({ nodeKey: editingNode.id, branch: 'true' })}>
                  Configurar Verdadeiro
                </button>
                <button className="btn btn--secondary btn--sm" onClick={() => setBranchModal({ nodeKey: editingNode.id, branch: 'false' })}>
                  Configurar Falso
                </button>
              </div>
              <div className="hint">Defina o destino de cada saída (ir para um nó ou desfecho final).</div>
            </div>
          )}

          <div className="row spread" style={{ marginTop: '1rem' }}>
            {editingNode.data.kind !== 'Start' && !readOnly ? (
              <button className="btn btn--danger" onClick={() => deleteNode(editingNode.id)}>Excluir nó</button>
            ) : (
              <span />
            )}
            <button className="btn btn--primary" onClick={() => setEditingNodeId(null)}>Fechar</button>
          </div>
        </Modal>
      )}

      {/* Branch decision modal */}
      {branchModal && (
        <BranchModal
          branch={branchModal.branch}
          nodeOptions={nodes
            .filter((n) => n.id !== branchModal.nodeKey)
            .map((n) => ({ key: n.id, label: `${n.data.label} (${n.data.kind})` }))}
          onCancel={() => setBranchModal(null)}
          onConfirm={applyBranch}
        />
      )}

      {/* Test decision modal */}
      {testOpen && (
        <Modal title="Testar decisão" onClose={() => setTestOpen(false)}>
          <DecisionRunner flowId={flowId!} />
        </Modal>
      )}
    </div>
  );
}

/** Editor page wrapped in the React Flow provider. */
export function EditorPage() {
  return (
    <ReactFlowProvider>
      <EditorInner />
    </ReactFlowProvider>
  );
}

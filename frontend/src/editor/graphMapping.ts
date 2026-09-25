import type { Edge, Node } from 'reactflow';
import type { GraphEdge, GraphNode, VersionGraph } from '../api/types';
import type { MotorNodeData } from './nodeTypes';

/** Converts the API graph into React Flow nodes/edges. */
export function toReactFlow(graph: VersionGraph): {
  nodes: Node<MotorNodeData>[];
  edges: Edge[];
} {
  const nodes = graph.nodes.map<Node<MotorNodeData>>((n) => ({
    id: n.nodeKey,
    type: 'motor',
    position: { x: n.positionX, y: n.positionY },
    data: { label: n.label, kind: n.kind },
  }));

  const edges = graph.edges.map<Edge>((e) => ({
    id: e.edgeKey,
    source: e.sourceNodeKey,
    target: e.targetNodeKey,
    sourceHandle: e.sourceHandle ?? undefined,
    label: e.label ?? e.sourceHandle ?? undefined,
  }));

  return { nodes, edges };
}

/**
 * Merges React Flow node/edge state back into the API graph, preserving the
 * per-node config/rulesetKey held in the config map (React Flow only tracks
 * label/position/kind visually).
 */
export function toVersionGraph(
  nodes: Node<MotorNodeData>[],
  edges: Edge[],
  configByNode: Record<string, { config: string; rulesetKey: string | null }>,
  rulesets: VersionGraph['rulesets'],
  formulas: VersionGraph['formulas'],
  inputFields: VersionGraph['inputFields'],
): VersionGraph {
  const graphNodes: GraphNode[] = nodes.map((n) => ({
    nodeKey: n.id,
    kind: n.data.kind,
    label: n.data.label,
    positionX: n.position.x,
    positionY: n.position.y,
    config: configByNode[n.id]?.config ?? '{}',
    rulesetKey: configByNode[n.id]?.rulesetKey ?? null,
  }));

  const graphEdges: GraphEdge[] = edges.map((e) => ({
    edgeKey: e.id,
    sourceNodeKey: e.source,
    targetNodeKey: e.target,
    sourceHandle: (e.sourceHandle as string | undefined) ?? null,
    label: typeof e.label === 'string' ? e.label : null,
  }));

  return { nodes: graphNodes, edges: graphEdges, rulesets, formulas, inputFields };
}

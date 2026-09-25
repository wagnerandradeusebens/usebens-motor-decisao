import { Handle, Position, type NodeProps } from 'reactflow';
import type { FlowNodeKind } from '../api/types';

export interface MotorNodeData {
  label: string;
  kind: FlowNodeKind;
}

/**
 * A single canvas node. Handles differ by kind:
 * - Start has only a source (bottom).
 * - Decision has only a target (top) — it is terminal.
 * - Condition has two labelled source handles: "true" and "false".
 * - Everything else has one target (top) and one source (bottom).
 */
function MotorNode({ data, selected }: NodeProps<MotorNodeData>) {
  const isStart = data.kind === 'Start';
  const isDecision = data.kind === 'Decision';
  const isCondition = data.kind === 'Condition';
  const isComment = data.kind === 'Comment';

  // A Comment is a pure annotation: no handles, styled like a sticky note.
  if (isComment) {
    return (
      <div className={`rf-node Comment ${selected ? 'selected' : ''}`}>
        <div className="kind">Comentário</div>
        <div className="comment-text">{data.label || 'Comentário…'}</div>
      </div>
    );
  }

  return (
    <div className={`rf-node ${data.kind} ${selected ? 'selected' : ''}`}>
      {!isStart && <Handle type="target" position={Position.Top} />}

      <div className="kind">{data.kind}</div>
      <div>{data.label}</div>

      {isCondition ? (
        <>
          <Handle
            id="true"
            type="source"
            position={Position.Bottom}
            style={{ left: '30%', background: '#22c55e' }}
          />
          <Handle
            id="false"
            type="source"
            position={Position.Bottom}
            style={{ left: '70%', background: '#ef4444' }}
          />
        </>
      ) : (
        !isDecision && <Handle type="source" position={Position.Bottom} />
      )}
    </div>
  );
}

export const nodeTypes = { motor: MotorNode };

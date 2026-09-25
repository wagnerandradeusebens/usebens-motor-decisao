import type {
  DecisionResponse,
  ExecutionDetail,
  ExecutionSummary,
  FlowSummary,
  FlowVersionSummary,
  GlobalVariable,
  SourceDescriptorDto,
  VersionGraph,
} from './types';

// All calls go through the Vite dev proxy at /api (rewritten to the backend
// root). In production, set VITE_API_BASE to the deployed API origin.
const BASE = (import.meta.env.VITE_API_BASE as string | undefined) ?? '/api';

/** Error carrying the backend's pt-BR message and HTTP status. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  });

  if (!res.ok) {
    let message = `Erro ${res.status}`;
    try {
      const body = await res.json();
      if (body && typeof body.error === 'string') {
        message = body.error;
      }
    } catch {
      // ignore non-JSON error bodies
    }
    throw new ApiError(res.status, message);
  }

  if (res.status === 204) {
    return undefined as T;
  }
  return (await res.json()) as T;
}

export const api = {
  // --- Flows ---
  listFlows: () => request<FlowSummary[]>('/flows'),

  getFlow: (flowId: string) => request<FlowSummary>(`/flows/${flowId}`),

  createFlow: (name: string, description: string | null) =>
    request<FlowSummary>('/flows', {
      method: 'POST',
      body: JSON.stringify({ name, description }),
    }),

  deleteFlow: (flowId: string) =>
    request<void>(`/flows/${flowId}`, { method: 'DELETE' }),

  // --- Versions / graph ---
  getVersionGraph: (flowId: string, versionId: string) =>
    request<VersionGraph>(`/flows/${flowId}/versions/${versionId}`),

  saveVersionGraph: (flowId: string, versionId: string, graph: VersionGraph) =>
    request<VersionGraph>(`/flows/${flowId}/versions/${versionId}`, {
      method: 'PUT',
      body: JSON.stringify(graph),
    }),

  createVersion: (flowId: string, copyFromVersionId?: string) =>
    request<FlowVersionSummary>(`/flows/${flowId}/versions`, {
      method: 'POST',
      body: JSON.stringify({ copyFromVersionId: copyFromVersionId ?? null }),
    }),

  publishVersion: (flowId: string, versionId: string) =>
    request<FlowVersionSummary>(`/flows/${flowId}/versions/${versionId}/publish`, {
      method: 'POST',
    }),

  // --- Decisions / audit ---
  decide: (flowId: string, proposalReference: string | null, fields: Record<string, unknown>) =>
    request<DecisionResponse>(`/flows/${flowId}/decisions`, {
      method: 'POST',
      body: JSON.stringify({ proposalReference, fields }),
    }),

  listExecutions: (flowId: string) =>
    request<ExecutionSummary[]>(`/flows/${flowId}/executions`),

  getExecution: (executionId: string) =>
    request<ExecutionDetail>(`/executions/${executionId}`),

  // --- Sources (read-only catalog) ---
  listSources: () => request<SourceDescriptorDto[]>('/sources'),

  // --- Global variables (shared across policies) ---
  listGlobalVariables: () => request<GlobalVariable[]>('/global-variables'),

  createGlobalVariable: (key: string, label: string, expression: string) =>
    request<GlobalVariable>('/global-variables', {
      method: 'POST',
      body: JSON.stringify({ key, label, expression }),
    }),

  updateGlobalVariable: (id: string, key: string, label: string, expression: string) =>
    request<GlobalVariable>(`/global-variables/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ key, label, expression }),
    }),

  deleteGlobalVariable: (id: string) =>
    request<void>(`/global-variables/${id}`, { method: 'DELETE' }),
};

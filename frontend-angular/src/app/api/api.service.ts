import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  DecisionResponse,
  ExecutionDetail,
  ExecutionSummary,
  FlowSummary,
  FlowVersionSummary,
  GlobalParameterTable,
  GlobalParameterTableInput,
  GlobalVariable,
  PolicyInputSchema,
  SourceConfigDto,
  SourceDescriptorDto,
  VersionGraph,
} from './models';

/**
 * Cliente HTTP tipado da API do motor de decisão. As chamadas passam pelo proxy
 * de dev em `/api` (reescrito para a raiz do backend). Em produção, ajustar a
 * base conforme o deploy. Espelha o cliente do front React (api/client.ts).
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // --- Flows ---
  listFlows(): Observable<FlowSummary[]> {
    return this.http.get<FlowSummary[]>(`${this.base}/flows`);
  }

  getFlow(flowId: string): Observable<FlowSummary> {
    return this.http.get<FlowSummary>(`${this.base}/flows/${flowId}`);
  }

  createFlow(name: string, description: string | null): Observable<FlowSummary> {
    return this.http.post<FlowSummary>(`${this.base}/flows`, { name, description });
  }

  deleteFlow(flowId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/flows/${flowId}`);
  }

  // --- Versions / graph ---
  getVersionGraph(flowId: string, versionId: string): Observable<VersionGraph> {
    return this.http.get<VersionGraph>(`${this.base}/flows/${flowId}/versions/${versionId}`);
  }

  /** Schema de entrada da versão: campos declarados + derivados das fontes. */
  getInputSchema(flowId: string, versionId: string): Observable<PolicyInputSchema> {
    return this.http.get<PolicyInputSchema>(
      `${this.base}/flows/${flowId}/versions/${versionId}/input-schema`,
    );
  }

  saveVersionGraph(flowId: string, versionId: string, graph: VersionGraph): Observable<VersionGraph> {
    return this.http.put<VersionGraph>(`${this.base}/flows/${flowId}/versions/${versionId}`, graph);
  }

  createVersion(flowId: string, copyFromVersionId?: string): Observable<FlowVersionSummary> {
    return this.http.post<FlowVersionSummary>(`${this.base}/flows/${flowId}/versions`, {
      copyFromVersionId: copyFromVersionId ?? null,
    });
  }

  publishVersion(flowId: string, versionId: string): Observable<FlowVersionSummary> {
    return this.http.post<FlowVersionSummary>(
      `${this.base}/flows/${flowId}/versions/${versionId}/publish`,
      {},
    );
  }

  /** Exclui uma versão (recusada pelo backend se publicada/vinculada). */
  deleteVersion(flowId: string, versionId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/flows/${flowId}/versions/${versionId}`);
  }

  /**
   * Testa uma versão específica (rascunho) sem publicar e sem persistir. Compila
   * e executa o grafo da versão na hora, devolvendo o mesmo formato de decisão.
   */
  testDecide(
    flowId: string,
    versionId: string,
    proposalReference: string | null,
    fields: Record<string, unknown>,
  ): Observable<DecisionResponse> {
    return this.http.post<DecisionResponse>(
      `${this.base}/flows/${flowId}/versions/${versionId}/test-decision`,
      { proposalReference, fields },
    );
  }

  // --- Decisions / audit ---
  decide(
    flowId: string,
    proposalReference: string | null,
    fields: Record<string, unknown>,
  ): Observable<DecisionResponse> {
    return this.http.post<DecisionResponse>(`${this.base}/flows/${flowId}/decisions`, {
      proposalReference,
      fields,
    });
  }

  listExecutions(flowId: string): Observable<ExecutionSummary[]> {
    return this.http.get<ExecutionSummary[]>(`${this.base}/flows/${flowId}/executions`);
  }

  getExecution(executionId: string): Observable<ExecutionDetail> {
    return this.http.get<ExecutionDetail>(`${this.base}/executions/${executionId}`);
  }

  // --- Sources (catálogo read-only + parâmetros operacionais) ---
  listSources(): Observable<SourceDescriptorDto[]> {
    return this.http.get<SourceDescriptorDto[]>(`${this.base}/sources`);
  }

  getSourceConfig(name: string): Observable<SourceConfigDto> {
    return this.http.get<SourceConfigDto>(`${this.base}/sources/${encodeURIComponent(name)}/config`);
  }

  updateSourceConfig(name: string, config: SourceConfigDto): Observable<SourceConfigDto> {
    return this.http.put<SourceConfigDto>(`${this.base}/sources/${encodeURIComponent(name)}/config`, config);
  }

  // --- Variáveis globais (compartilhadas entre políticas) ---
  listGlobalVariables(): Observable<GlobalVariable[]> {
    return this.http.get<GlobalVariable[]>(`${this.base}/global-variables`);
  }

  createGlobalVariable(key: string, label: string, expression: string): Observable<GlobalVariable> {
    return this.http.post<GlobalVariable>(`${this.base}/global-variables`, { key, label, expression });
  }

  updateGlobalVariable(
    id: string,
    key: string,
    label: string,
    expression: string,
  ): Observable<GlobalVariable> {
    return this.http.put<GlobalVariable>(`${this.base}/global-variables/${id}`, { key, label, expression });
  }

  deleteGlobalVariable(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/global-variables/${id}`);
  }

  // --- Tabelas de parâmetros globais (compartilhadas entre políticas) ---
  listGlobalTables(): Observable<GlobalParameterTable[]> {
    return this.http.get<GlobalParameterTable[]>(`${this.base}/global-tables`);
  }

  createGlobalTable(input: GlobalParameterTableInput): Observable<GlobalParameterTable> {
    return this.http.post<GlobalParameterTable>(`${this.base}/global-tables`, input);
  }

  updateGlobalTable(id: string, input: GlobalParameterTableInput): Observable<GlobalParameterTable> {
    return this.http.put<GlobalParameterTable>(`${this.base}/global-tables/${id}`, input);
  }

  deleteGlobalTable(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/global-tables/${id}`);
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AgentDefinition,
  ConsoleEvent,
  DashboardOverview,
  OmniAgentSettings,
  ProviderHealthStatus,
  RuntimeAgent,
  TaskDetail,
  TaskStatusSnapshot,
  TaskSummary,
  UpdateAgentDefinitionRequest,
  UpdateOmniAgentApiKeyResponse,
  UsageSummary,
  ModelDefinition,
  WorkspaceNode,
  ApiCredential,
  SkillDefinition,
  SaveSkillRequest,
  SuggestSkillsResponse,
  ProjectDetectResponse,
  ProjectRunStatusResponse,
  ProjectRunActionResponse,
  ProjectProxyResponse,
  AgentGroupSummary,
  AgentGroupDetail,
  AgentGroupMember,
  UpsertAgentGroupMemberRequest,
  PanelSessionSummary,
  PanelSessionDetail
} from '../models';

const API_BASE_URL = '/api';

@Injectable({ providedIn: 'root' })
export class TaskApiClient {
  private readonly http = inject(HttpClient);

  createTask(prompt: string, inputContextJson?: string): Observable<TaskSummary> {
    return this.http.post<TaskSummary>(`${API_BASE_URL}/tasks`, { prompt, inputContextJson });
  }

  listAvailableModels(): Observable<{ id: string; ownedBy: string; registered: boolean }[]> {
    return this.http.get<{ id: string; ownedBy: string; registered: boolean }[]>(`${API_BASE_URL}/agents/models/available`);
  }

  syncModelsFromProvider(): Observable<{ imported: number; totalAvailable: number }> {
    return this.http.post<{ imported: number; totalAvailable: number }>(`${API_BASE_URL}/agents/models/sync`, {});
  }

  listSkills(): Observable<SkillDefinition[]> {
    return this.http.get<SkillDefinition[]>(`${API_BASE_URL}/skills`);
  }

  createSkill(request: SaveSkillRequest): Observable<SkillDefinition> {
    return this.http.post<SkillDefinition>(`${API_BASE_URL}/skills`, request);
  }

  updateSkill(id: string, request: SaveSkillRequest): Observable<SkillDefinition> {
    return this.http.put<SkillDefinition>(`${API_BASE_URL}/skills/${id}`, request);
  }

  deleteSkill(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/skills/${id}`);
  }

  suggestSkills(prompt: string): Observable<SuggestSkillsResponse> {
    return this.http.post<SuggestSkillsResponse>(`${API_BASE_URL}/skills/suggest`, { prompt });
  }

  runTask(taskId: string): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/tasks/${taskId}/run`, {});
  }

  continueTask(taskId: string, prompt: string): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/tasks/${taskId}/continue`, { prompt });
  }

  cancelTask(taskId: string): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/tasks/${taskId}/cancel`, {});
  }

  getTaskEvents(taskId: string): Observable<ConsoleEvent[]> {
    return this.http.get<ConsoleEvent[]>(`${API_BASE_URL}/tasks/${taskId}/events`);
  }

  getTask(taskId: string): Observable<TaskDetail> {
    return this.http.get<TaskDetail>(`${API_BASE_URL}/tasks/${taskId}`);
  }

  /** Cheap status probe for Studio polling (no heavy graph). */
  getTaskStatus(taskId: string): Observable<TaskStatusSnapshot> {
    return this.http.get<TaskStatusSnapshot>(`${API_BASE_URL}/tasks/${taskId}/status`);
  }

  listTasks(): Observable<TaskSummary[]> {
    return this.http.get<TaskSummary[]>(`${API_BASE_URL}/tasks`);
  }

  renameTask(taskId: string, title: string): Observable<unknown> {
    return this.http.put(`${API_BASE_URL}/tasks/${taskId}/title`, { title });
  }

  deleteTask(taskId: string): Observable<unknown> {
    return this.http.delete(`${API_BASE_URL}/tasks/${taskId}`);
  }

  listRuntimeAgents(): Observable<RuntimeAgent[]> {
    return this.http.get<RuntimeAgent[]>(`${API_BASE_URL}/agents/runtime`);
  }

  listAgentDefinitions(): Observable<AgentDefinition[]> {
    return this.http.get<AgentDefinition[]>(`${API_BASE_URL}/agents`);
  }

  updateAgentDefinition(agentId: string, request: UpdateAgentDefinitionRequest): Observable<AgentDefinition> {
    return this.http.put<AgentDefinition>(`${API_BASE_URL}/agents/${agentId}`, request);
  }

  createAgentDefinition(request: UpdateAgentDefinitionRequest): Observable<AgentDefinition> {
    return this.http.post<AgentDefinition>(`${API_BASE_URL}/agents`, request);
  }

  deleteAgentDefinition(agentId: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/agents/${agentId}`);
  }

  getUsageSummary(): Observable<UsageSummary> {
    return this.http.get<UsageSummary>(`${API_BASE_URL}/usage/summary`);
  }

  getDashboardOverview(): Observable<DashboardOverview> {
    return this.http.get<DashboardOverview>(`${API_BASE_URL}/dashboard/overview`);
  }

  getSettings(): Observable<OmniAgentSettings> {
    return this.http.get<OmniAgentSettings>(`${API_BASE_URL}/settings`);
  }

  updateOmniAgentApiKey(apiKey: string): Observable<UpdateOmniAgentApiKeyResponse> {
    return this.http.put<UpdateOmniAgentApiKeyResponse>(`${API_BASE_URL}/settings/omniagent/api-key`, { apiKey });
  }

  checkOmniAgentHealth(): Observable<ProviderHealthStatus> {
    return this.http.post<ProviderHealthStatus>(`${API_BASE_URL}/settings/omniagent/health`, {});
  }

  listModels(): Observable<ModelDefinition[]> {
    return this.http.get<ModelDefinition[]>(`${API_BASE_URL}/agents/models`);
  }

  addModel(model: string, displayName: string, contextWindow?: number | null): Observable<ModelDefinition> {
    return this.http.post<ModelDefinition>(`${API_BASE_URL}/agents/models`, { model, displayName, contextWindow });
  }

  deleteModel(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/agents/models/${id}`);
  }

  getWorkspaceFiles(): Observable<WorkspaceNode[]> {
    return this.http.get<WorkspaceNode[]>(`${API_BASE_URL}/workspace/files`);
  }

  getWorkspaceFileContent(path: string): Observable<{ content: string }> {
    return this.http.get<{ content: string }>(`${API_BASE_URL}/workspace/file`, { params: { path } });
  }

  deleteWorkspaceNode(path: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/workspace`, { params: { path } });
  }

  detectWorkspaceProject(path?: string | null): Observable<ProjectDetectResponse> {
    const params: Record<string, string> = {};
    if (path) {
      params['path'] = path;
    }
    return this.http.get<ProjectDetectResponse>(`${API_BASE_URL}/workspace/project`, { params });
  }

  workspaceProjectUp(path?: string | null): Observable<ProjectRunActionResponse> {
    const params: Record<string, string> = {};
    if (path) {
      params['path'] = path;
    }
    return this.http.post<ProjectRunActionResponse>(`${API_BASE_URL}/workspace/project/up`, {}, { params });
  }

  workspaceProjectDown(path?: string | null): Observable<ProjectRunActionResponse> {
    const params: Record<string, string> = {};
    if (path) {
      params['path'] = path;
    }
    return this.http.post<ProjectRunActionResponse>(`${API_BASE_URL}/workspace/project/down`, {}, { params });
  }

  workspaceProjectStatus(path?: string | null): Observable<ProjectRunStatusResponse> {
    const params: Record<string, string> = {};
    if (path) {
      params['path'] = path;
    }
    return this.http.get<ProjectRunStatusResponse>(`${API_BASE_URL}/workspace/project/status`, { params });
  }

  workspaceProjectProxy(request: {
    projectPath?: string | null;
    method: string;
    path: string;
    headers?: Record<string, string>;
    body?: string | null;
  }): Observable<ProjectProxyResponse> {
    return this.http.post<ProjectProxyResponse>(`${API_BASE_URL}/workspace/project/proxy`, {
      projectPath: request.projectPath,
      method: request.method,
      path: request.path,
      headers: request.headers,
      body: request.body
    });
  }

  listCredentials(): Observable<ApiCredential[]> {
    return this.http.get<ApiCredential[]>(`${API_BASE_URL}/credentials`);
  }

  createCredential(request: { name: string; provider: string; baseUrl?: string; apiKey: string; isDefault?: boolean }): Observable<ApiCredential> {
    return this.http.post<ApiCredential>(`${API_BASE_URL}/credentials`, request);
  }

  updateCredential(id: string, request: { name: string; provider: string; baseUrl?: string; apiKey?: string; isDefault?: boolean }): Observable<ApiCredential> {
    return this.http.put<ApiCredential>(`${API_BASE_URL}/credentials/${id}`, request);
  }

  deleteCredential(id: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/credentials/${id}`);
  }

  // ── Agent Groups (panel personas) ─────────────────────────────────────

  listAgentGroups(): Observable<AgentGroupSummary[]> {
    return this.http.get<AgentGroupSummary[]>(`${API_BASE_URL}/agent-groups`);
  }

  getAgentGroup(groupId: string): Observable<AgentGroupDetail> {
    return this.http.get<AgentGroupDetail>(`${API_BASE_URL}/agent-groups/${groupId}`);
  }

  createAgentGroup(name: string, description?: string | null): Observable<AgentGroupDetail> {
    return this.http.post<AgentGroupDetail>(`${API_BASE_URL}/agent-groups`, { name, description });
  }

  updateAgentGroup(groupId: string, name: string, description?: string | null): Observable<AgentGroupDetail> {
    return this.http.put<AgentGroupDetail>(`${API_BASE_URL}/agent-groups/${groupId}`, { name, description });
  }

  deleteAgentGroup(groupId: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/agent-groups/${groupId}`);
  }

  cloneAgentGroup(groupId: string): Observable<AgentGroupDetail> {
    return this.http.post<AgentGroupDetail>(`${API_BASE_URL}/agent-groups/${groupId}/clone`, {});
  }

  addGroupMember(groupId: string, request: UpsertAgentGroupMemberRequest): Observable<AgentGroupMember> {
    return this.http.post<AgentGroupMember>(`${API_BASE_URL}/agent-groups/${groupId}/members`, request);
  }

  updateGroupMember(
    groupId: string,
    memberId: string,
    request: UpsertAgentGroupMemberRequest
  ): Observable<AgentGroupMember> {
    return this.http.put<AgentGroupMember>(
      `${API_BASE_URL}/agent-groups/${groupId}/members/${memberId}`,
      request
    );
  }

  deleteGroupMember(groupId: string, memberId: string): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/agent-groups/${groupId}/members/${memberId}`);
  }

  reorderGroupMembers(groupId: string, memberIdsInOrder: string[]): Observable<AgentGroupDetail> {
    return this.http.put<AgentGroupDetail>(`${API_BASE_URL}/agent-groups/${groupId}/members/reorder`, {
      memberIdsInOrder
    });
  }

  // ── Moderated Panel ───────────────────────────────────────────────────

  listPanels(): Observable<PanelSessionSummary[]> {
    return this.http.get<PanelSessionSummary[]>(`${API_BASE_URL}/panels`);
  }

  getPanel(panelId: string): Observable<PanelSessionDetail> {
    return this.http.get<PanelSessionDetail>(`${API_BASE_URL}/panels/${panelId}`);
  }

  getPanelEvents(panelId: string): Observable<ConsoleEvent[]> {
    return this.http.get<ConsoleEvent[]>(`${API_BASE_URL}/panels/${panelId}/events`);
  }

  createPanel(
    groupId: string,
    topic: string,
    title?: string | null,
    maxRounds: number = 1
  ): Observable<PanelSessionDetail> {
    return this.http.post<PanelSessionDetail>(`${API_BASE_URL}/panels`, {
      groupId,
      topic,
      title,
      maxRounds
    });
  }

  startPanel(panelId: string): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/panels/${panelId}/start`, {});
  }

  continuePanel(panelId: string, message: string, extraRounds: number = 1): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/panels/${panelId}/continue`, { message, extraRounds });
  }

  cancelPanel(panelId: string): Observable<unknown> {
    return this.http.post(`${API_BASE_URL}/panels/${panelId}/cancel`, {});
  }

  getPanelTranscript(panelId: string): Observable<string> {
    return this.http.get(`${API_BASE_URL}/panels/${panelId}/transcript`, {
      responseType: 'text'
    });
  }
}

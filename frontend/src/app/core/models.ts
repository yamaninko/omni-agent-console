export type TaskStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type AgentStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type ModelCallStatus = 'Started' | 'Succeeded' | 'Failed' | 'Cancelled';

export interface TaskSummary {
  id: string;
  title: string;
  status: TaskStatus;
  createdAt: string;
  completedAt?: string | null;
  totalTokens: number;
  totalLatencyMs: number;
}

export interface ConsoleEvent {
  id: string;
  taskRunId: string;
  agentRunId?: string | null;
  eventType: string;
  message: string;
  payloadJson?: string | null;
  createdAt: string;
}

export interface AgentRunDetail {
  id: string;
  agentName: string;
  agentType: string;
  status: AgentStatus;
  input?: string | null;
  output?: string | null;
  executionOrder: number;
  startedAt?: string | null;
  completedAt?: string | null;
  latencyMs: number;
  errorMessage?: string | null;
}

export interface ModelCallLogDetail {
  id: string;
  agentRunId: string;
  agentName: string;
  provider: string;
  model: string;
  requestType: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  latencyMs: number;
  status: ModelCallStatus;
  errorCode: string;
  errorMessage?: string | null;
  startedAt: string;
  completedAt?: string | null;
  estimatedCost: number;
}

export interface TaskDetail {
  id: string;
  title: string;
  inputPrompt: string;
  inputContextJson?: string | null;
  status: TaskStatus;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  totalLatencyMs: number;
  errorMessage?: string | null;
  agentRuns: AgentRunDetail[];
  modelCallLogs: ModelCallLogDetail[];
  consoleEvents: ConsoleEvent[];
}

export interface RuntimeAgent {
  name: string;
  type: string;
  description: string;
  model?: string;
}

export interface AgentDefinition {
  id: string;
  name: string;
  type: string;
  description: string;
  enabled: boolean;
  defaultModel: string;
  systemPrompt: string;
  maxTokens: number;
  temperature: number;
  timeoutSeconds: number;
  retryCount: number;
  createdAt: string;
  updatedAt?: string | null;
  provider: string;
  customApiUrl?: string;
  customApiKeyConfigured?: boolean;
  apiCredentialId?: string | null;
  fallbackModels?: string | null;
}

export interface UpdateAgentDefinitionRequest {
  enabled: boolean;
  defaultModel: string;
  systemPrompt: string;
  maxTokens: number;
  temperature: number;
  timeoutSeconds: number;
  retryCount: number;
  provider: string;
  customApiUrl?: string;
  customApiKey?: string;
  apiCredentialId?: string | null;
  fallbackModels?: string;
  name?: string;
  description?: string;
  type?: string;
}

export interface SkillDefinition {
  id: string;
  name: string;
  category: string;
  description: string;
  instructions: string;
  keywords: string;
  enabled: boolean;
  sortOrder: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveSkillRequest {
  name: string;
  category?: string;
  description?: string;
  instructions: string;
  keywords?: string;
  enabled: boolean;
  sortOrder: number;
}

export interface SuggestSkillsResponse {
  skillIds: string[];
  questions: string[];
}

export interface ApiCredential {
  id: string;
  name: string;
  provider: string;
  baseUrl?: string;
  apiKeyConfigured: boolean;
  maskedApiKey?: string | null;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface UsageSummary {
  totalRequests: number;
  successRate: number;
  averageLatencyMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  errorCount: number;
  activeTaskCount: number;
}

export interface DashboardOverview {
  totalTasks: number;
  runningTasks: number;
  completedTasks: number;
  failedTasks: number;
  cancelledTasks: number;
  totalRequests: number;
  successRate: number;
  averageLatencyMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  errorCount: number;
  agentBreakdown: AgentUsageBreakdown[];
  modelBreakdown: ModelUsageBreakdown[];
  recentTasks: TaskSummary[];
}

export interface AgentUsageBreakdown {
  agentName: string;
  agentType: string;
  requests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageLatencyMs: number;
  errorCount: number;
}

export interface ModelUsageBreakdown {
  provider: string;
  model: string;
  requests: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageLatencyMs: number;
  errorCount: number;
}

export interface OmniAgentSettings {
  provider: string;
  baseUrl: string;
  defaultModel: string;
  apiKeySecretName: string;
  apiKeyConfigured: boolean;
  secretStore: string;
  timeoutSeconds: number;
  retryCount: number;
}

export interface UpdateOmniAgentApiKeyResponse {
  apiKeyConfigured: boolean;
  secretStore: string;
  secretName: string;
}

export interface ProviderHealthStatus {
  provider: string;
  model: string;
  apiKeyConfigured: boolean;
  healthy: boolean;
  status: string;
  message: string;
  latencyMs: number;
  checkedAt: string;
}

export interface ModelDefinition {
  id: string;
  model: string;
  displayName: string;
  contextWindow?: number | null;
}

export interface WorkspaceNode {
  name: string;
  path: string;
  isDirectory: boolean;
  children?: WorkspaceNode[] | null;
}

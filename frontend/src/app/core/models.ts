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
  /** Sum of model-call estimated costs (USD). */
  estimatedCost?: number;
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
  /** Sum of model-call estimated costs (USD). */
  estimatedCost?: number;
}

/** Lightweight poll payload — no agent I/O / console / model logs. */
export interface TaskStatusSnapshot {
  id: string;
  title: string;
  status: TaskStatus;
  completedAt?: string | null;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  totalLatencyMs: number;
  errorMessage?: string | null;
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
  estimatedCostTotal?: number;
  livePanelSessions?: number;
  liveTaskSessions?: number;
  liveSessions?: LiveSessionRow[];
}

export interface LiveSessionRow {
  kind: 'task' | 'panel' | string;
  id: string;
  title: string;
  status: string;
  createdAt: string;
  ownerSessionId?: string | null;
  totalTokens: number;
  estimatedCost: number;
}

export interface DemoSeedResult {
  groupId: string;
  groupName: string;
  created: boolean;
  suggestedTopic: string;
  studioPrompt: string;
  studioPipeline: string;
  workspacePath: string;
}

export interface StudioDemoPreset {
  id: string;
  name: string;
  pipeline: string;
  workspacePath: string;
  prompt: string;
  skillKeywords: string[];
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
  /** SHARED_LAB / SharedLab:Enabled */
  sharedLabEnabled?: boolean;
  /** Instructor (console key); students are false when shared-lab is on. */
  isAdmin?: boolean;
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

export interface ProjectRouteHint {
  method: string;
  path: string;
  label: string;
  exampleBody?: string | null;
}

export interface ProjectDetectResponse {
  projectRoot: string;
  hasDockerfile: boolean;
  hasCompose: boolean;
  runnable: boolean;
  suggestedHostPort: number;
  composeProjectName: string;
  healthUrl: string;
  upCommand: string;
  downCommand: string;
  statusCommand: string;
  message?: string | null;
  projectKind: 'api' | 'web' | 'hybrid' | 'unknown' | string;
  baseUrl: string;
  openUrl: string;
  suggestedRoutes: ProjectRouteHint[];
}

export interface ProjectProxyResponse {
  ok: boolean;
  statusCode: number;
  latencyMs: number;
  contentType?: string | null;
  body: string;
  headers: Record<string, string>;
  error?: string | null;
}

export interface ProjectRunStatusResponse {
  projectRoot: string;
  composeProjectName: string;
  hostPort: number;
  state: string;
  runnerEnabled: boolean;
  healthUrl?: string | null;
  healthStatus?: string | null;
  detail?: string | null;
  logsTail?: string | null;
}

export interface ProjectRunActionResponse {
  ok: boolean;
  state: string;
  message: string;
  logsTail?: string | null;
}

/** Agent Groups + moderated panel (orthogonal to Studio pipeline agents). */
export interface AgentGroupSummary {
  id: string;
  name: string;
  description?: string | null;
  memberCount: number;
  createdAt: string;
  updatedAt?: string | null;
  isTemplate?: boolean;
}

/** Panel role: moderator opens; commentators debate with a stance. */
export type PanelMemberRole = 'Moderator' | 'Commentator';

/** Debate side — multiple members may share For; one may take Against. */
export type PanelStance = 'Neutral' | 'For' | 'Against' | 'Custom';

export interface AgentGroupMember {
  id: string;
  groupId: string;
  displayName: string;
  systemPrompt: string;
  defaultModel: string;
  fallbackModels?: string | null;
  provider: string;
  apiCredentialId?: string | null;
  maxTokens: number;
  temperature: number;
  timeoutSeconds: number;
  retryCount: number;
  sortOrder: number;
  enabled: boolean;
  role: PanelMemberRole | string;
  stance: PanelStance | string;
  stanceLabel?: string | null;
  createdAt: string;
}

export interface AgentGroupDetail {
  id: string;
  name: string;
  description?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  members: AgentGroupMember[];
  isTemplate?: boolean;
}

export interface UpsertAgentGroupMemberRequest {
  displayName: string;
  systemPrompt: string;
  defaultModel: string;
  fallbackModels?: string | null;
  provider: string;
  apiCredentialId?: string | null;
  maxTokens: number;
  temperature: number;
  timeoutSeconds: number;
  retryCount: number;
  sortOrder: number;
  enabled: boolean;
  role: PanelMemberRole | string;
  stance: PanelStance | string;
  stanceLabel?: string | null;
}

export type PanelSessionStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

export interface PanelSessionSummary {
  id: string;
  groupId: string;
  groupName: string;
  title: string;
  topic: string;
  status: PanelSessionStatus | string;
  maxRounds?: number;
  createdAt: string;
  completedAt?: string | null;
  totalTokens: number;
  totalLatencyMs: number;
}

export interface PanelTurn {
  id: string;
  memberId: string;
  memberDisplayName: string;
  turnOrder: number;
  output?: string | null;
  status: string;
  modelUsed?: string | null;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  latencyMs: number;
  errorMessage?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface PanelVoteTally {
  memberId: string;
  displayName: string;
  votes: number;
}

export interface PanelSessionDetail {
  id: string;
  groupId: string;
  groupName: string;
  title: string;
  topic: string;
  status: PanelSessionStatus | string;
  maxRounds?: number;
  currentMemberId?: string | null;
  floorDeadline?: string | null;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  totalLatencyMs: number;
  errorMessage?: string | null;
  turns: PanelTurn[];
  consoleEvents: ConsoleEvent[];
  /** Audience "who convinced you" tallies. */
  votes?: PanelVoteTally[];
}

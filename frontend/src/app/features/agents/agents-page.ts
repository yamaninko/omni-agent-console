import { Component, OnInit, inject, signal } from '@angular/core';
import { Bot, KeyRound, LucideAngularModule, Save, Trash2, Plus, Play, Sparkles, X, ToggleLeft, ToggleRight, Check } from 'lucide-angular';
import { TaskApiClient } from '../../core/api/task-api-client';
import { I18nService } from '../../core/i18n/i18n.service';
import { AgentDefinition, ModelDefinition, ApiCredential } from '../../core/models';
import { DialogService } from '../../core/ui/dialog.service';

@Component({
  selector: 'app-agents-page',
  imports: [LucideAngularModule],
  templateUrl: './agents-page.html',
  styleUrl: './agents-page.scss'
})
export class AgentsPage implements OnInit {
  private readonly api = inject(TaskApiClient);
  private readonly dialog = inject(DialogService);
  private readonly i18n = inject(I18nService);

  protected t(key: string): string {
    return this.i18n.t(key);
  }

  protected readonly icons = {
    bot: Bot,
    key: KeyRound,
    save: Save,
    trash: Trash2,
    plus: Plus,
    play: Play,
    sparkles: Sparkles,
    x: X,
    toggleLeft: ToggleLeft,
    toggleRight: ToggleRight,
    check: Check
  };

  protected readonly agents = signal<AgentDefinition[]>([]);
  protected readonly models = signal<ModelDefinition[]>([]);
  protected readonly credentials = signal<ApiCredential[]>([]);
  protected readonly selectedAgent = signal<AgentDefinition | null>(null);
  protected readonly isCreating = signal(false);

  // Status flags
  protected readonly saving = signal(false);
  protected readonly deleting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly success = signal<string | null>(null);

  // Form Fields for Create / Edit
  protected readonly formName = signal('');
  protected readonly formDescription = signal('');
  protected readonly formType = signal('Coder');
  protected readonly formEnabled = signal(true);
  protected readonly formDefaultModel = signal('');
  protected readonly formSystemPrompt = signal('');
  protected readonly formMaxTokens = signal(4096);
  protected readonly formTemperature = signal(0.2);
  protected readonly formTimeoutSeconds = signal(120);
  protected readonly formRetryCount = signal(2);
  protected readonly formProvider = signal('OmniAgent');
  protected readonly formCustomApiUrl = signal('');
  protected readonly formCustomApiKey = signal('');
  protected readonly formApiCredentialId = signal<string | null>(null);
  protected readonly formFallbackModel1 = signal('');
  protected readonly formFallbackModel2 = signal('');

  protected readonly agentTypes = ['Planner', 'Research', 'Coder', 'Reviewer', 'OpsMonitor'];
  protected readonly providers = [
    { value: 'OmniAgent', label: 'OMNIAGENT NIM' },
    { value: 'OpenAi', label: 'OpenAI' },
    { value: 'OpenAI', label: 'OpenAI (External)' },
    { value: 'Anthropic', label: 'Anthropic' },
    { value: 'AzureOpenAi', label: 'Azure OpenAI' },
    { value: 'Ollama', label: 'Ollama (Local)' },
    { value: 'LocalNim', label: 'Local NIM' },
    { value: 'Vllm', label: 'vLLM (Local)' },
    { value: 'Gemini', label: 'Gemini' },
    { value: 'Custom', label: 'Custom / OpenAI-Compatible' }
  ];

  ngOnInit(): void {
    this.loadAgents();
    this.loadModels();
    this.loadCredentials();
  }

  private loadCredentials(): void {
    this.api.listCredentials().subscribe({
      next: (creds) => this.credentials.set(creds),
      error: () => this.error.set('Failed to load saved API credentials.')
    });
  }

  private loadAgents(): void {
    this.api.listAgentDefinitions().subscribe({
      next: (data) => {
        this.agents.set(data);
        if (data.length > 0 && !this.selectedAgent()) {
          this.selectAgent(data[0]);
        }
      },
      error: () => this.error.set('Failed to load agent definitions.')
    });
  }

  private loadModels(): void {
    this.api.listModels().subscribe({
      next: (data) => this.models.set(data),
      error: () => this.error.set('Failed to load global models.')
    });
  }

  protected selectAgent(agent: AgentDefinition): void {
    this.isCreating.set(false);
    this.selectedAgent.set(agent);
    this.error.set(null);
    this.success.set(null);

    // Populate form fields
    this.formName.set(agent.name);
    this.formDescription.set(agent.description);
    this.formType.set(agent.type);
    this.formEnabled.set(agent.enabled);
    this.formDefaultModel.set(agent.defaultModel);
    this.formSystemPrompt.set(agent.systemPrompt);
    this.formMaxTokens.set(agent.maxTokens);
    this.formTemperature.set(agent.temperature);
    this.formTimeoutSeconds.set(agent.timeoutSeconds);
    this.formRetryCount.set(agent.retryCount);
    this.formApiCredentialId.set(agent.apiCredentialId ?? null);

    const fallbacks = (agent.fallbackModels ?? '').split(',').map(m => m.trim()).filter(Boolean);
    this.formFallbackModel1.set(fallbacks[0] ?? '');
    this.formFallbackModel2.set(fallbacks[1] ?? '');

    // Raw API keys never leave the backend; the key input stays blank and an
    // empty value on save means "keep the stored key".
    this.formCustomApiKey.set('');

    if (agent.apiCredentialId) {
      const cred = this.credentials().find(c => c.id === agent.apiCredentialId);
      if (cred) {
        this.formProvider.set(cred.provider);
        this.formCustomApiUrl.set(cred.baseUrl ?? '');
      } else {
        this.formProvider.set(agent.provider);
        this.formCustomApiUrl.set(agent.customApiUrl ?? '');
      }
    } else {
      this.formProvider.set(agent.provider);
      this.formCustomApiUrl.set(agent.customApiUrl ?? '');
    }
  }

  protected startCreation(): void {
    this.isCreating.set(true);
    this.selectedAgent.set(null);
    this.error.set(null);
    this.success.set(null);

    // Reset form fields with defaults
    this.formName.set('New Coder Agent');
    this.formDescription.set('Generates source code files');
    this.formType.set('Coder');
    this.formEnabled.set(true);
    this.formDefaultModel.set('');
    this.formSystemPrompt.set('You are the coder agent. Generate high quality code.');
    this.formMaxTokens.set(4096);
    this.formTemperature.set(0.2);
    this.formTimeoutSeconds.set(120);
    this.formRetryCount.set(2);
    this.formProvider.set('OmniAgent');
    this.formCustomApiUrl.set('');
    this.formCustomApiKey.set('');
    this.formApiCredentialId.set(null);
    this.formFallbackModel1.set('');
    this.formFallbackModel2.set('');
  }

  protected saveAgent(): void {
    this.error.set(null);
    this.success.set(null);

    const name = this.formName().trim();
    const description = this.formDescription().trim();
    const systemPrompt = this.formSystemPrompt().trim();
    const defaultModel = this.formDefaultModel().trim();

    if (!name) {
      this.error.set('Agent Name is required.');
      return;
    }
    if (!systemPrompt) {
      this.error.set('System Prompt is required.');
      return;
    }

    const providerVal = this.formProvider();
    let mappedProvider = providerVal;
    if (providerVal === 'Gemini' || providerVal === 'Custom' || providerVal === 'OpenAI') {
      mappedProvider = 'OpenAi';
    }

    const payload = {
      enabled: this.formEnabled(),
      defaultModel,
      systemPrompt,
      maxTokens: this.formMaxTokens(),
      temperature: this.formTemperature(),
      timeoutSeconds: this.formTimeoutSeconds(),
      retryCount: this.formRetryCount(),
      provider: mappedProvider,
      customApiUrl: this.formCustomApiUrl().trim() || undefined,
      customApiKey: this.formCustomApiKey().trim() || undefined,
      apiCredentialId: this.formApiCredentialId() || null,
      fallbackModels: [this.formFallbackModel1().trim(), this.formFallbackModel2().trim()]
        .filter(Boolean).join(',') || undefined,
      name,
      description,
      type: this.formType()
    };

    this.saving.set(true);

    if (this.isCreating()) {
      this.api.createAgentDefinition(payload).subscribe({
        next: (newAgent) => {
          this.saving.set(false);
          this.success.set('Agent created successfully.');
          this.agents.update(list => [...list, newAgent]);
          this.selectAgent(newAgent);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err.error?.detail || err.error || 'Failed to create agent.');
        }
      });
    } else {
      const active = this.selectedAgent();
      if (!active) return;

      this.api.updateAgentDefinition(active.id, payload).subscribe({
        next: (updatedAgent) => {
          this.saving.set(false);
          this.success.set('Agent updated successfully.');
          this.agents.update(list => list.map(a => a.id === updatedAgent.id ? updatedAgent : a));
          this.selectAgent(updatedAgent);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err.error?.detail || err.error || 'Failed to update agent.');
        }
      });
    }
  }

  protected async deleteAgent(): Promise<void> {
    const active = this.selectedAgent();
    if (!active) return;

    const ok = await this.dialog.confirm({
      title: 'Delete agent',
      message: `Are you sure you want to delete agent "${active.name}"? This cannot be undone.`,
      confirmLabel: 'Delete',
      cancelLabel: 'Cancel',
      danger: true
    });
    if (!ok) {
      return;
    }

    this.deleting.set(true);
    this.error.set(null);
    this.success.set(null);

    this.api.deleteAgentDefinition(active.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.success.set('Agent deleted successfully.');
        const remaining = this.agents().filter(a => a.id !== active.id);
        this.agents.set(remaining);
        if (remaining.length > 0) {
          this.selectAgent(remaining[0]);
        } else {
          this.selectedAgent.set(null);
          this.isCreating.set(false);
        }
      },
      error: () => {
        this.deleting.set(false);
        this.error.set('Failed to delete agent.');
      }
    });
  }

  protected isCustomModel(model: string): boolean {
    if (!model) return true;
    return !this.models().some(m => m.model === model);
  }

  protected getDefaultCredentialName(): string {
    const def = this.credentials().find(c => c.isDefault);
    return def ? `${def.name} (${def.provider})` : 'System Settings';
  }

  protected onModelSelectChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    if (val !== '__custom__') {
      this.formDefaultModel.set(val);
    } else {
      this.formDefaultModel.set('');
    }
  }

  // Input Update Handlers
  protected updateField(field: string, event: Event): void {
    const val = (event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;
    switch (field) {
      case 'name': this.formName.set(val); break;
      case 'description': this.formDescription.set(val); break;
      case 'type': this.formType.set(val); break;
      case 'defaultModel': this.formDefaultModel.set(val); break;
      case 'systemPrompt': this.formSystemPrompt.set(val); break;
      case 'provider': this.formProvider.set(val); break;
      case 'customApiUrl': this.formCustomApiUrl.set(val); break;
      case 'customApiKey': this.formCustomApiKey.set(val); break;
      case 'fallbackModel1': this.formFallbackModel1.set(val); break;
      case 'fallbackModel2': this.formFallbackModel2.set(val); break;
      case 'apiCredentialId':
        this.formApiCredentialId.set(val || null);
        if (val) {
          const cred = this.credentials().find(c => c.id === val);
          if (cred) {
            this.formProvider.set(cred.provider);
            this.formCustomApiUrl.set(cred.baseUrl ?? '');
            // The linked credential's key is resolved server-side; never mirror it here.
            this.formCustomApiKey.set('');
          }
        }
        break;
    }
  }

  protected updateNumberField(field: string, event: Event): void {
    const val = parseFloat((event.target as HTMLInputElement).value);
    if (isNaN(val)) return;
    switch (field) {
      case 'maxTokens': this.formMaxTokens.set(Math.floor(val)); break;
      case 'temperature': this.formTemperature.set(val); break;
      case 'timeoutSeconds': this.formTimeoutSeconds.set(Math.floor(val)); break;
      case 'retryCount': this.formRetryCount.set(Math.floor(val)); break;
    }
  }

  protected toggleEnabled(): void {
    this.formEnabled.set(!this.formEnabled());
  }
}

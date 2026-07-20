import { Component, OnInit, inject, signal } from '@angular/core';
import { Activity, KeyRound, LucideAngularModule, Save, ShieldCheck } from 'lucide-angular';
import { TaskApiClient } from '../../core/api/task-api-client';
import { AgentDefinition, OmniAgentSettings, ProviderHealthStatus, ModelDefinition, ApiCredential, SkillDefinition } from '../../core/models';

@Component({
  selector: 'app-settings-page',
  imports: [LucideAngularModule],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss'
})
export class SettingsPage implements OnInit {
  private readonly api = inject(TaskApiClient);

  protected readonly icons = {
    activity: Activity,
    key: KeyRound,
    save: Save,
    vault: ShieldCheck
  };

  protected readonly settings = signal<OmniAgentSettings | null>(null);
  protected readonly apiKey = signal('');
  protected readonly consoleApiKey = signal(localStorage.getItem('console_api_key') ?? '');
  protected readonly models = signal<ModelDefinition[]>([]);
  protected readonly credentials = signal<ApiCredential[]>([]);
  protected readonly health = signal<ProviderHealthStatus | null>(null);
  protected readonly saving = signal(false);
  protected readonly checking = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly newModelKey = signal('');
  protected readonly newModelDisplayName = signal('');
  protected readonly newModelContext = signal<number | null>(null);

  protected readonly newCredName = signal('');
  protected readonly newCredProvider = signal('OmniAgent');
  protected readonly newCredBaseUrl = signal('');
  protected readonly newCredApiKey = signal('');
  protected readonly newCredIsDefault = signal(false);
  protected readonly editingCredentialId = signal<string | null>(null);

  protected readonly syncingModels = signal(false);

  protected syncModelsFromProvider(): void {
    if (this.syncingModels()) {
      return;
    }

    this.syncingModels.set(true);
    this.message.set(null);
    this.error.set(null);

    this.api.syncModelsFromProvider().subscribe({
      next: (result) => {
        this.syncingModels.set(false);
        this.message.set(`Imported ${result.imported} new model(s); provider exposes ${result.totalAvailable} models.`);
        this.loadModels();
      },
      error: (err) => {
        this.syncingModels.set(false);
        this.error.set(err.error || 'Failed to sync models from provider.');
      }
    });
  }

  protected readonly skillList = signal<SkillDefinition[]>([]);
  protected readonly editingSkillId = signal<string | null>(null);
  protected readonly skillName = signal('');
  protected readonly skillCategory = signal('General');
  protected readonly skillDescription = signal('');
  protected readonly skillInstructions = signal('');
  protected readonly skillKeywords = signal('');
  protected readonly skillEnabled = signal(true);

  protected saveSkill(): void {
    const name = this.skillName().trim();
    const instructions = this.skillInstructions().trim();
    if (!name || !instructions) {
      this.error.set('Skill name and instructions are required.');
      return;
    }

    this.message.set(null);
    this.error.set(null);

    const request = {
      name,
      category: this.skillCategory().trim() || 'General',
      description: this.skillDescription().trim(),
      instructions,
      keywords: this.skillKeywords().trim(),
      enabled: this.skillEnabled(),
      sortOrder: this.skillList().length
    };

    const editId = this.editingSkillId();
    const call = editId ? this.api.updateSkill(editId, request) : this.api.createSkill(request);
    call.subscribe({
      next: () => {
        this.cancelSkillEdit();
        this.message.set(editId ? 'Skill updated.' : 'Skill added.');
        this.loadSkills();
      },
      error: () => this.error.set('Failed to save skill.')
    });
  }

  protected editSkill(s: SkillDefinition): void {
    this.editingSkillId.set(s.id);
    this.skillName.set(s.name);
    this.skillCategory.set(s.category);
    this.skillDescription.set(s.description);
    this.skillInstructions.set(s.instructions);
    this.skillKeywords.set(s.keywords);
    this.skillEnabled.set(s.enabled);
  }

  protected cancelSkillEdit(): void {
    this.editingSkillId.set(null);
    this.skillName.set('');
    this.skillCategory.set('General');
    this.skillDescription.set('');
    this.skillInstructions.set('');
    this.skillKeywords.set('');
    this.skillEnabled.set(true);
  }

  protected toggleSkillEnabled(s: SkillDefinition): void {
    this.api.updateSkill(s.id, {
      name: s.name,
      category: s.category,
      description: s.description,
      instructions: s.instructions,
      keywords: s.keywords,
      enabled: !s.enabled,
      sortOrder: s.sortOrder
    }).subscribe({
      next: () => this.loadSkills(),
      error: () => this.error.set('Failed to update skill.')
    });
  }

  protected deleteSkill(id: string): void {
    this.api.deleteSkill(id).subscribe({
      next: () => {
        if (this.editingSkillId() === id) {
          this.cancelSkillEdit();
        }
        this.message.set('Skill removed.');
        this.loadSkills();
      },
      error: () => this.error.set('Failed to delete skill.')
    });
  }

  private loadSkills(): void {
    this.api.listSkills().subscribe({
      next: (skills) => this.skillList.set(skills),
      error: () => this.error.set('Skills could not be loaded.')
    });
  }

  protected updateNewModelKey(event: Event): void {
    this.newModelKey.set((event.target as HTMLInputElement).value);
  }

  protected updateNewModelDisplayName(event: Event): void {
    this.newModelDisplayName.set((event.target as HTMLInputElement).value);
  }

  protected updateNewModelContext(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.newModelContext.set(val ? parseInt(val, 10) : null);
  }

  protected addModel(): void {
    const key = this.newModelKey().trim();
    const name = this.newModelDisplayName().trim();
    const ctx = this.newModelContext();

    if (!key || !name) {
      this.error.set('Model ID and Display Name are required.');
      return;
    }

    this.message.set(null);
    this.error.set(null);

    this.api.addModel(key, name, ctx).subscribe({
      next: () => {
        this.newModelKey.set('');
        this.newModelDisplayName.set('');
        this.newModelContext.set(null);
        this.message.set('Model definition added successfully.');
        this.loadModels();
      },
      error: () => this.error.set('Failed to add model definition.')
    });
  }

  protected deleteModel(id: string): void {
    this.message.set(null);
    this.error.set(null);

    this.api.deleteModel(id).subscribe({
      next: () => {
        this.message.set('Model definition removed.');
        this.loadModels();
      },
      error: () => this.error.set('Failed to delete model definition.')
    });
  }

  protected isCustomModel(modelName: string): boolean {
    return !this.models().some(m => m.model === modelName);
  }

  ngOnInit(): void {
    this.loadSettings();
    this.loadModels();
    this.loadCredentials();
    this.loadSkills();
  }

  protected addCredential(): void {
    const name = this.newCredName().trim();
    const provider = this.newCredProvider().trim();
    const baseUrl = this.newCredBaseUrl().trim();
    const apiKey = this.newCredApiKey().trim();
    const isDefault = this.newCredIsDefault();

    const editId = this.editingCredentialId();

    // The API never returns raw keys, so the edit form starts blank;
    // an empty key while editing means "keep the stored key".
    if (!name || !provider || (!apiKey && !editId)) {
      this.error.set('Friendly Name, Provider, and API Key are required.');
      return;
    }

    this.message.set(null);
    this.error.set(null);

    const payload = { name, provider, baseUrl: baseUrl || undefined, apiKey: apiKey || undefined, isDefault };

    if (editId) {
      this.api.updateCredential(editId, payload).subscribe({
        next: () => {
          this.cancelEdit();
          this.message.set('API Credential updated successfully.');
          this.loadCredentials();
        },
        error: () => this.error.set('Failed to update API Credential.')
      });
    } else {
      this.api.createCredential({ ...payload, apiKey }).subscribe({
        next: () => {
          this.newCredName.set('');
          this.newCredBaseUrl.set('');
          this.newCredApiKey.set('');
          this.newCredIsDefault.set(false);
          this.message.set('API Credential added successfully.');
          this.loadCredentials();
        },
        error: () => this.error.set('Failed to add API Credential.')
      });
    }
  }

  protected editCredential(c: ApiCredential): void {
    this.editingCredentialId.set(c.id);
    this.newCredName.set(c.name);
    this.newCredProvider.set(c.provider);
    this.newCredBaseUrl.set(c.baseUrl ?? '');
    this.newCredApiKey.set('');
    this.newCredIsDefault.set(c.isDefault);
  }

  protected cancelEdit(): void {
    this.editingCredentialId.set(null);
    this.newCredName.set('');
    this.newCredProvider.set('OmniAgent');
    this.newCredBaseUrl.set('');
    this.newCredApiKey.set('');
    this.newCredIsDefault.set(false);
  }

  protected deleteCredential(id: string): void {
    this.message.set(null);
    this.error.set(null);

    this.api.deleteCredential(id).subscribe({
      next: () => {
        this.message.set('API Credential removed.');
        if (this.editingCredentialId() === id) {
          this.cancelEdit();
        }
        this.loadCredentials();
      },
      error: () => this.error.set('Failed to delete API Credential.')
    });
  }

  private loadCredentials(): void {
    this.api.listCredentials().subscribe({
      next: (creds) => this.credentials.set(creds),
      error: () => this.error.set('API Credentials could not be loaded.')
    });
  }

  protected updateApiKey(event: Event): void {
    this.apiKey.set((event.target as HTMLInputElement).value);
  }

  protected updateConsoleApiKey(event: Event): void {
    this.consoleApiKey.set((event.target as HTMLInputElement).value);
  }

  protected saveConsoleApiKey(): void {
    const key = this.consoleApiKey().trim();
    if (key) {
      localStorage.setItem('console_api_key', key);
    } else {
      localStorage.removeItem('console_api_key');
    }
    this.message.set('Console API Key updated locally in browser.');
  }

  protected hasModel(modelName: string): boolean {
    return this.models().some(m => m.model === modelName);
  }

  protected saveApiKey(): void {
    const key = this.apiKey().trim();
    if (!key || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.message.set(null);
    this.error.set(null);

    this.api.updateOmniAgentApiKey(key).subscribe({
      next: (response) => {
        this.apiKey.set('');
        this.message.set(`API key stored in ${response.secretStore}.`);
        this.loadSettings();
        this.checkHealth();
      },
      error: () => {
        this.error.set('API key could not be stored. Check Vault connectivity.');
        this.saving.set(false);
      },
      complete: () => this.saving.set(false)
    });
  }

  protected checkHealth(): void {
    if (this.checking()) {
      return;
    }

    this.checking.set(true);
    this.error.set(null);

    this.api.checkOmniAgentHealth().subscribe({
      next: (health) => {
        this.health.set(health);
        this.checking.set(false);
      },
      // complete never fires on error; clearing here keeps the button usable.
      error: () => {
        this.error.set('OMNIAGENT health check failed before reaching the provider.');
        this.checking.set(false);
      }
    });
  }

  private loadSettings(): void {
    this.api.getSettings().subscribe({
      next: (settings) => this.settings.set(settings),
      error: () => this.error.set('Settings could not be loaded.')
    });
  }

  private loadModels(): void {
    this.api.listModels().subscribe({
      next: (models) => this.models.set(models),
      error: () => this.error.set('Model definitions could not be loaded.')
    });
  }
}

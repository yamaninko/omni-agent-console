import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  Bot,
  Check,
  ChevronDown,
  ChevronUp,
  LucideAngularModule,
  Plus,
  Save,
  Trash2,
  Users,
  X
} from 'lucide-angular';
import { Subscription } from 'rxjs';
import { TaskApiClient } from '../../core/api/task-api-client';
import {
  AgentGroupDetail,
  AgentGroupMember,
  AgentGroupSummary,
  ApiCredential,
  ModelDefinition,
  UpsertAgentGroupMemberRequest
} from '../../core/models';
import { DialogService } from '../../core/ui/dialog.service';

interface GroupTemplateMember {
  displayName: string;
  role: 'Moderator' | 'Commentator';
  stance: 'Neutral' | 'For' | 'Against' | 'Custom';
  stanceLabel: string | null;
  systemPrompt: string;
  defaultModel: string;
  sortOrder: number;
}

interface GroupTemplate {
  id: string;
  name: string;
  description: string;
  members: GroupTemplateMember[];
}

@Component({
  selector: 'app-groups-page',
  imports: [LucideAngularModule],
  templateUrl: './groups-page.html',
  styleUrl: './groups-page.scss'
})
export class GroupsPage implements OnInit, OnDestroy {
  private readonly api = inject(TaskApiClient);
  private readonly dialog = inject(DialogService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private routeSub?: Subscription;

  protected readonly icons = {
    bot: Bot,
    users: Users,
    plus: Plus,
    save: Save,
    trash: Trash2,
    check: Check,
    x: X,
    up: ChevronUp,
    down: ChevronDown
  };

  protected readonly groups = signal<AgentGroupSummary[]>([]);
  protected readonly selected = signal<AgentGroupDetail | null>(null);
  protected readonly models = signal<ModelDefinition[]>([]);
  protected readonly credentials = signal<ApiCredential[]>([]);
  protected readonly isCreatingGroup = signal(false);
  protected readonly isCreatingMember = signal(false);
  protected readonly editingMemberId = signal<string | null>(null);

  protected readonly groupName = signal('');
  protected readonly groupDescription = signal('');

  protected readonly memberName = signal('');
  protected readonly memberPrompt = signal('');
  protected readonly memberModel = signal('meta/llama-3.1-8b-instruct');
  protected readonly memberFallback = signal('');
  protected readonly memberProvider = signal('OmniAgent');
  protected readonly memberCredentialId = signal<string | null>(null);
  protected readonly memberMaxTokens = signal(800);
  protected readonly memberTemperature = signal(0.7);
  protected readonly memberTimeout = signal(60);
  protected readonly memberRetry = signal(1);
  protected readonly memberEnabled = signal(true);
  protected readonly memberRole = signal<'Moderator' | 'Commentator'>('Commentator');
  protected readonly memberStance = signal<'Neutral' | 'For' | 'Against' | 'Custom'>('Neutral');
  protected readonly memberStanceLabel = signal('');

  protected readonly roles = [
    { value: 'Moderator' as const, label: 'Moderator (opens the panel, introduces roster)' },
    { value: 'Commentator' as const, label: 'Commentator (debates with a stance)' }
  ];

  protected readonly stances = [
    { value: 'Neutral' as const, label: 'Neutral (no forced side)' },
    { value: 'For' as const, label: 'For / Pro (affirmative)' },
    { value: 'Against' as const, label: 'Against / Con (opposing)' },
    { value: 'Custom' as const, label: 'Custom (describe below)' }
  ];

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly success = signal<string | null>(null);

  protected readonly providers = [
    { value: 'OmniAgent', label: 'OMNIAGENT NIM' },
    { value: 'OpenAi', label: 'OpenAI' },
    { value: 'Ollama', label: 'Ollama' },
    { value: 'Custom', label: 'Custom / OpenAI-Compatible' }
  ];

  /** One-click cast presets (clone into a new editable group). */
  protected readonly templates: GroupTemplate[] = [
    {
      id: '3-1',
      name: '3-for / 1-against',
      description: 'Moderator + three affirmative + one opposing voice. Classic debate imbalance.',
      members: [
        {
          displayName: 'Moderator',
          role: 'Moderator',
          stance: 'Neutral',
          stanceLabel: null,
          systemPrompt:
            'You moderate a live panel. Introduce the roster and topic, keep speakers on-mission, and close with a crisp synthesis. Do not invent guests.',
          defaultModel: 'meta/llama-3.1-8b-instruct',
          sortOrder: 0
        },
        {
          displayName: 'Advocate A',
          role: 'Commentator',
          stance: 'For',
          stanceLabel: 'affirmative case',
          systemPrompt:
            'You argue FOR the motion. Bring concrete reasons, examples, and steelman the case. Stay in character; do not invent other guests.',
          defaultModel: 'meta/llama-3.1-8b-instruct',
          sortOrder: 1
        },
        {
          displayName: 'Advocate B',
          role: 'Commentator',
          stance: 'For',
          stanceLabel: 'affirmative case',
          systemPrompt:
            'You argue FOR the motion from a second angle (policy, economics, or human impact). Avoid repeating Advocate A.',
          defaultModel: 'openai/gpt-oss-120b',
          sortOrder: 2
        },
        {
          displayName: 'Advocate C',
          role: 'Commentator',
          stance: 'For',
          stanceLabel: 'affirmative case',
          systemPrompt:
            'You argue FOR the motion with a third perspective (ethics, history, or tech). Stay specific to the topic.',
          defaultModel: 'deepseek-ai/deepseek-v4-flash',
          sortOrder: 3
        },
        {
          displayName: 'Critic',
          role: 'Commentator',
          stance: 'Against',
          stanceLabel: 'opposing case',
          systemPrompt:
            'You argue AGAINST the motion. Challenge assumptions, demand evidence, and name risks. Stay civil and on-topic.',
          defaultModel: 'stepfun-ai/step-3.7-flash',
          sortOrder: 4
        }
      ]
    },
    {
      id: '2v2',
      name: '2v2 balanced',
      description: 'Moderator + two for + two against. Equal floor time for both sides.',
      members: [
        {
          displayName: 'Moderator',
          role: 'Moderator',
          stance: 'Neutral',
          stanceLabel: null,
          systemPrompt:
            'You moderate a balanced debate. Open with roster + rules, keep both sides fair, and summarize trade-offs without picking a winner unless asked.',
          defaultModel: 'meta/llama-3.1-8b-instruct',
          sortOrder: 0
        },
        {
          displayName: 'Pro 1',
          role: 'Commentator',
          stance: 'For',
          stanceLabel: 'pro',
          systemPrompt: 'Argue FOR the motion with clear claims and evidence. Engage opponents by name when relevant.',
          defaultModel: 'meta/llama-3.1-8b-instruct',
          sortOrder: 1
        },
        {
          displayName: 'Pro 2',
          role: 'Commentator',
          stance: 'For',
          stanceLabel: 'pro',
          systemPrompt: 'Second FOR voice — different evidence and frame than Pro 1. No guest invention.',
          defaultModel: 'openai/gpt-oss-120b',
          sortOrder: 2
        },
        {
          displayName: 'Con 1',
          role: 'Commentator',
          stance: 'Against',
          stanceLabel: 'con',
          systemPrompt: 'Argue AGAINST the motion. Attack weakest claims; offer alternatives.',
          defaultModel: 'deepseek-ai/deepseek-v4-flash',
          sortOrder: 3
        },
        {
          displayName: 'Con 2',
          role: 'Commentator',
          stance: 'Against',
          stanceLabel: 'con',
          systemPrompt: 'Second AGAINST voice — costs, risks, and unintended consequences.',
          defaultModel: 'stepfun-ai/step-3.7-flash',
          sortOrder: 4
        }
      ]
    }
  ];

  ngOnInit(): void {
    this.reloadGroups();
    this.api.listModels().subscribe({ next: (m) => this.models.set(m) });
    this.api.listCredentials().subscribe({ next: (c) => this.credentials.set(c) });

    this.routeSub = this.route.paramMap.subscribe((params) => {
      const groupId = params.get('groupId');
      if (!groupId) {
        return;
      }
      if (this.selected()?.id === groupId && !this.isCreatingGroup()) {
        return;
      }
      this.loadGroupById(groupId);
    });
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
  }

  protected reloadGroups(): void {
    this.api.listAgentGroups().subscribe({
      next: (groups) => this.groups.set(groups),
      error: (err) => this.error.set(err?.error || err?.message || 'Failed to load groups')
    });
  }

  protected startCreateGroup(): void {
    this.isCreatingGroup.set(true);
    this.selected.set(null);
    this.groupName.set('');
    this.groupDescription.set('');
    this.clearMemberForm();
    void this.router.navigate(['/groups']);
  }

  protected selectGroup(summary: AgentGroupSummary): void {
    void this.router.navigate(['/groups', summary.id]);
  }

  protected saveGroup(): void {
    const name = this.groupName().trim();
    if (!name) {
      this.error.set('Group name is required.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    const desc = this.groupDescription().trim() || null;

    if (this.isCreatingGroup()) {
      this.api.createAgentGroup(name, desc).subscribe({
        next: (detail) => {
          this.saving.set(false);
          this.success.set('Group created.');
          this.isCreatingGroup.set(false);
          this.selected.set(detail);
          this.reloadGroups();
          void this.router.navigate(['/groups', detail.id], { replaceUrl: true });
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err?.error || 'Create failed');
        }
      });
      return;
    }

    const id = this.selected()?.id;
    if (!id) return;

    this.api.updateAgentGroup(id, name, desc).subscribe({
      next: (detail) => {
        this.saving.set(false);
        this.success.set('Group updated.');
        this.selected.set(detail);
        this.reloadGroups();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(err?.error || 'Update failed');
      }
    });
  }

  protected async deleteGroup(): Promise<void> {
    const id = this.selected()?.id;
    if (!id) return;
    const ok = await this.dialog.confirm({
      title: 'Delete group?',
      message: 'This removes the group and its personas. Past panel sessions keep their history.',
      confirmLabel: 'Delete'
    });
    if (!ok) return;

    this.api.deleteAgentGroup(id).subscribe({
      next: () => {
        this.selected.set(null);
        this.success.set('Group deleted.');
        this.reloadGroups();
        void this.router.navigate(['/groups']);
      },
      error: (err) => this.error.set(err?.error || 'Delete failed')
    });
  }

  protected cloneGroup(): void {
    const id = this.selected()?.id;
    if (!id) return;
    this.saving.set(true);
    this.api.cloneAgentGroup(id).subscribe({
      next: (detail) => {
        this.saving.set(false);
        this.success.set('Group cloned.');
        this.reloadGroups();
        void this.router.navigate(['/groups', detail.id]);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(err?.error || 'Clone failed');
      }
    });
  }

  protected openInPanel(): void {
    const id = this.selected()?.id;
    if (!id) return;
    void this.router.navigate(['/panel'], { queryParams: { groupId: id } });
  }

  protected applyTemplate(template: GroupTemplate): void {
    if (this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.success.set(null);

    this.api.createAgentGroup(template.name, template.description).subscribe({
      next: (group) => {
        const members = [...template.members].sort((a, b) => a.sortOrder - b.sortOrder);
        const addNext = (index: number) => {
          if (index >= members.length) {
            this.saving.set(false);
            this.success.set(`Template “${template.name}” created — edit speakers, then open Panel.`);
            this.reloadGroups();
            void this.router.navigate(['/groups', group.id]);
            this.api.getAgentGroup(group.id).subscribe({
              next: (d) => {
                this.selected.set(d);
                this.isCreatingGroup.set(false);
                this.groupName.set(d.name);
                this.groupDescription.set(d.description ?? '');
              }
            });
            return;
          }
          const m = members[index];
          const request: UpsertAgentGroupMemberRequest = {
            displayName: m.displayName,
            systemPrompt: m.systemPrompt,
            defaultModel: m.defaultModel,
            fallbackModels: null,
            provider: 'OmniAgent',
            apiCredentialId: null,
            maxTokens: 800,
            temperature: 0.7,
            timeoutSeconds: 60,
            retryCount: 1,
            sortOrder: m.sortOrder,
            enabled: true,
            role: m.role,
            stance: m.stance,
            stanceLabel: m.stanceLabel
          };
          this.api.addGroupMember(group.id, request).subscribe({
            next: () => addNext(index + 1),
            error: (err) => {
              this.saving.set(false);
              this.error.set(err?.error || 'Template member failed');
              this.reloadGroups();
              void this.router.navigate(['/groups', group.id]);
            }
          });
        };
        addNext(0);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(err?.error || 'Template create failed');
      }
    });
  }

  protected startAddMember(): void {
    this.isCreatingMember.set(true);
    this.editingMemberId.set(null);
    this.clearMemberForm();
  }

  protected editMember(member: AgentGroupMember): void {
    this.isCreatingMember.set(false);
    this.editingMemberId.set(member.id);
    this.memberName.set(member.displayName);
    this.memberPrompt.set(member.systemPrompt);
    this.memberModel.set(member.defaultModel);
    this.memberFallback.set(member.fallbackModels ?? '');
    this.memberProvider.set(member.provider);
    this.memberCredentialId.set(member.apiCredentialId ?? null);
    this.memberMaxTokens.set(member.maxTokens);
    this.memberTemperature.set(member.temperature);
    this.memberTimeout.set(member.timeoutSeconds);
    this.memberRetry.set(member.retryCount);
    this.memberEnabled.set(member.enabled);
    this.memberRole.set(member.role === 'Moderator' ? 'Moderator' : 'Commentator');
    const stance = (member.stance || 'Neutral') as 'Neutral' | 'For' | 'Against' | 'Custom';
    this.memberStance.set(
      stance === 'For' || stance === 'Against' || stance === 'Custom' || stance === 'Neutral'
        ? stance
        : 'Neutral'
    );
    this.memberStanceLabel.set(member.stanceLabel ?? '');
  }

  protected saveMember(): void {
    const groupId = this.selected()?.id;
    if (!groupId) return;

    const request: UpsertAgentGroupMemberRequest = {
      displayName: this.memberName().trim(),
      systemPrompt: this.memberPrompt().trim(),
      defaultModel: this.memberModel().trim(),
      fallbackModels: this.memberFallback().trim() || null,
      provider: this.memberProvider(),
      apiCredentialId: this.memberCredentialId(),
      maxTokens: this.memberMaxTokens(),
      temperature: this.memberTemperature(),
      timeoutSeconds: this.memberTimeout(),
      retryCount: this.memberRetry(),
      sortOrder: 0,
      enabled: this.memberEnabled(),
      role: this.memberRole(),
      stance: this.memberStance(),
      stanceLabel: this.memberStanceLabel().trim() || null
    };

    if (!request.displayName || !request.systemPrompt || !request.defaultModel) {
      this.error.set('Name, system prompt, and model are required.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    const done = () => {
      this.saving.set(false);
      this.clearMemberForm();
      this.api.getAgentGroup(groupId).subscribe({
        next: (d) => {
          this.selected.set(d);
          this.reloadGroups();
        }
      });
    };

    const editId = this.editingMemberId();
    if (editId) {
      this.api.updateGroupMember(groupId, editId, request).subscribe({
        next: () => {
          this.success.set('Speaker updated.');
          done();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err?.error || 'Update failed');
        }
      });
    } else {
      this.api.addGroupMember(groupId, request).subscribe({
        next: () => {
          this.success.set('Speaker added.');
          done();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err?.error || 'Add failed');
        }
      });
    }
  }

  protected async deleteMember(member: AgentGroupMember): Promise<void> {
    const groupId = this.selected()?.id;
    if (!groupId) return;
    const ok = await this.dialog.confirm({
      title: 'Remove speaker?',
      message: `Remove ${member.displayName} from this group?`,
      confirmLabel: 'Remove'
    });
    if (!ok) return;

    this.api.deleteGroupMember(groupId, member.id).subscribe({
      next: () => {
        this.success.set('Speaker removed.');
        this.clearMemberForm();
        this.api.getAgentGroup(groupId).subscribe({
          next: (d) => {
            this.selected.set(d);
            this.reloadGroups();
          }
        });
      },
      error: (err) => this.error.set(err?.error || 'Delete failed')
    });
  }

  protected moveMember(member: AgentGroupMember, delta: number): void {
    const group = this.selected();
    if (!group) return;
    const ordered = this.orderedMembers(group);
    const idx = ordered.findIndex((m) => m.id === member.id);
    const swap = idx + delta;
    if (idx < 0 || swap < 0 || swap >= ordered.length) return;

    const ids = ordered.map((m) => m.id);
    [ids[idx], ids[swap]] = [ids[swap], ids[idx]];

    this.api.reorderGroupMembers(group.id, ids).subscribe({
      next: (detail) => {
        this.selected.set(detail);
        this.success.set('Speaking order updated.');
      },
      error: (err) => this.error.set(err?.error || 'Reorder failed')
    });
  }

  /** Stable display order: moderators first, then sortOrder (matches panel runtime). */
  protected orderedMembers(group: AgentGroupDetail | null): AgentGroupMember[] {
    if (!group?.members?.length) return [];
    return [...group.members].sort((a, b) => {
      const ra = a.role === 'Moderator' ? 0 : 1;
      const rb = b.role === 'Moderator' ? 0 : 1;
      if (ra !== rb) return ra - rb;
      if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder;
      return a.displayName.localeCompare(b.displayName);
    });
  }

  protected roleLabel(role: string | undefined): string {
    return role === 'Moderator' ? 'Moderator' : 'Commentator';
  }

  protected roleMission(role: string | undefined): string {
    return role === 'Moderator'
      ? 'Opens the panel, restates the topic, introduces only the real roster by name/role/stance.'
      : 'Debates the session topic in character; defends the assigned stance (or maps it onto today’s topic).';
  }

  protected stanceLine(member: AgentGroupMember): string {
    const stance = member.stance || 'Neutral';
    const label = member.stanceLabel?.trim();
    if (label) return `${stance} — ${label}`;
    return stance;
  }

  /** First sentence / short blurb of the persona system prompt. */
  protected personaBlurb(prompt: string | undefined): string {
    if (!prompt?.trim()) return 'No persona text yet.';
    let t = prompt.trim().replace(/\s+/g, ' ');
    const cut = t.search(/[.!?]/);
    if (cut > 0 && cut < 200) {
      return t.slice(0, cut + 1);
    }
    return t.length <= 180 ? t : t.slice(0, 177).trimEnd() + '…';
  }

  protected shortId(id: string): string {
    return (id || '').replace(/-/g, '').slice(0, 8);
  }

  protected async copyGroupId(): Promise<void> {
    const id = this.selected()?.id;
    if (!id) return;
    try {
      await navigator.clipboard.writeText(id);
      this.success.set('Group id copied.');
    } catch {
      this.error.set(id);
    }
  }

  protected async copyGroupLink(): Promise<void> {
    const id = this.selected()?.id;
    if (!id) return;
    const url = `${window.location.origin}/groups/${id}`;
    try {
      await navigator.clipboard.writeText(url);
      this.success.set('Group link copied.');
    } catch {
      this.error.set(url);
    }
  }

  private loadGroupById(groupId: string): void {
    this.isCreatingGroup.set(false);
    this.error.set(null);
    this.api.getAgentGroup(groupId).subscribe({
      next: (detail) => {
        this.selected.set(detail);
        this.groupName.set(detail.name);
        this.groupDescription.set(detail.description ?? '');
        this.clearMemberForm();
      },
      error: (err) => {
        this.selected.set(null);
        this.error.set(err?.error || 'Failed to load group');
      }
    });
  }

  private clearMemberForm(): void {
    this.isCreatingMember.set(false);
    this.editingMemberId.set(null);
    this.memberName.set('');
    this.memberPrompt.set(
      'You are a thoughtful panel guest. Speak in character with clear, concise arguments.'
    );
    this.memberModel.set('meta/llama-3.1-8b-instruct');
    this.memberFallback.set('');
    this.memberProvider.set('OmniAgent');
    this.memberCredentialId.set(null);
    this.memberMaxTokens.set(800);
    this.memberTemperature.set(0.7);
    this.memberTimeout.set(60);
    this.memberRetry.set(1);
    this.memberEnabled.set(true);
    this.memberRole.set('Commentator');
    this.memberStance.set('Neutral');
    this.memberStanceLabel.set('');
  }
}

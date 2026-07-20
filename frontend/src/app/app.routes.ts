import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'studio'
  },
  {
    path: 'studio',
    loadComponent: () => import('./features/studio/studio-page').then((m) => m.StudioPage)
  },
  {
    path: 'history',
    loadComponent: () => import('./features/task-history/task-history-page').then((m) => m.TaskHistoryPage)
  },
  {
    path: 'tasks/:id',
    loadComponent: () => import('./features/task-detail/task-detail-page').then((m) => m.TaskDetailPage)
  },
  {
    path: 'dashboard',
    loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage)
  },
  {
    path: 'settings',
    loadComponent: () => import('./features/settings/settings-page').then((m) => m.SettingsPage)
  },
  {
    path: 'agents',
    loadComponent: () => import('./features/agents/agents-page').then((m) => m.AgentsPage)
  },
  {
    path: 'docs',
    loadComponent: () => import('./features/docs/docs-page').then((m) => m.DocsPage)
  },
  {
    path: 'workspace',
    loadComponent: () => import('./features/workspace/workspace-page').then((m) => m.WorkspacePage)
  }
];

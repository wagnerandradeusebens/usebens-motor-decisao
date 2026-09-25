import { Routes } from '@angular/router';
import { Shell } from './shell/shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'politicas' },
      {
        path: 'politicas',
        loadComponent: () => import('./pages/policies/policy-list').then((m) => m.PolicyList),
      },
      {
        path: 'politicas/:flowId',
        loadComponent: () => import('./pages/policies/policy-detail').then((m) => m.PolicyDetail),
      },
      {
        path: 'politicas/:flowId/execucoes',
        loadComponent: () => import('./pages/executions/executions-page').then((m) => m.ExecutionsPage),
      },
      {
        path: 'politicas/:flowId/versions/:versionId',
        loadComponent: () => import('./editor/graph-editor').then((m) => m.GraphEditor),
      },
      {
        path: 'fontes',
        loadComponent: () => import('./pages/sources/sources-page').then((m) => m.SourcesPage),
      },
      {
        path: 'variaveis-globais',
        loadComponent: () =>
          import('./pages/global-variables/global-variables-page').then((m) => m.GlobalVariablesPage),
      },
    ],
  },
  { path: '**', redirectTo: 'politicas' },
];

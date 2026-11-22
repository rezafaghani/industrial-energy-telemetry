import type { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  {
    path: 'overview',
    loadComponent: () => import('./features/overview/overview-page.component').then(module => module.OverviewPageComponent),
  },
  {
    path: 'opc-ua',
    loadComponent: () => import('./features/infrastructure/infrastructure-page.component')
      .then(module => module.InfrastructurePageComponent),
  },
  {
    path: 'services',
    loadComponent: () => import('./features/services/services-page.component').then(module => module.ServicesPageComponent),
  },
  {
    path: 'series-explorer',
    loadComponent: () => import('./features/series-explorer/series-explorer-page.component')
      .then(module => module.SeriesExplorerPageComponent),
  },
  { path: '**', redirectTo: 'overview' },
];

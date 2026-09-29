import { Routes } from '@angular/router';

import { authGuard } from './core/auth.guard';

/**
 * Feature routes are lazily loaded, so the initial bundle carries the shell and the sign-in page
 * only. The list, detail and report views are each a separate chunk because a user who only ever
 * looks at one of them should not download the other three.
 */
export const routes: Routes = [
  {
    path: 'sign-in',
    loadComponent: () => import('./features/sign-in/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'policyholders',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/policyholders/policy-holder-list').then((m) => m.PolicyHolderList),
  },
  {
    path: 'policyholders/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/policyholders/policy-holder-detail').then((m) => m.PolicyHolderDetail),
  },
  {
    path: 'policies',
    canActivate: [authGuard],
    loadComponent: () => import('./features/policies/policy-list').then((m) => m.PolicyList),
  },
  {
    path: 'policies/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/policies/policy-detail').then((m) => m.PolicyDetail),
  },
  {
    path: 'claims',
    canActivate: [authGuard],
    loadComponent: () => import('./features/claims/claim-list').then((m) => m.ClaimList),
  },
  {
    path: 'claims/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/claims/claim-detail').then((m) => m.ClaimDetail),
  },
  {
    path: 'reports',
    canActivate: [authGuard],
    loadComponent: () => import('./features/reports/reports').then((m) => m.Reports),
  },
  { path: '', pathMatch: 'full', redirectTo: 'policyholders' },
  { path: '**', redirectTo: 'policyholders' },
];

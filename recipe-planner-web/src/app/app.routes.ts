import { Routes } from '@angular/router';
import { authGuard } from './auth.guard';
import { LoginPage } from './pages/login';
import { RegisterPage } from './pages/register';
import { RecipeListPage } from './pages/recipe-list';
import { RecipeFormPage } from './pages/recipe-form';
import { TechniqueListPage } from './pages/technique-list';
import { MealPlanListPage } from './pages/meal-plan-list';
import { MealPlanDetailPage } from './pages/meal-plan-detail';
import { ShoppingListPage } from './pages/shopping-list';
import { FridgePage } from './pages/fridge';
import { SharedRecipePage } from './pages/shared-recipe';
import { GroceryListPage } from './pages/grocery-list';
import { LibraryListPage } from './pages/library-list';
import { LibraryDetailPage } from './pages/library-detail';

export const routes: Routes = [
  { path: '', redirectTo: 'recipes', pathMatch: 'full' },
  { path: 'login', component: LoginPage },
  { path: 'register', component: RegisterPage },
  { path: 'forgot-password', loadComponent: () => import('./pages/forgot-password').then(m => m.ForgotPasswordPage) },
  { path: 'confirm-email', loadComponent: () => import('./pages/confirm-email').then(m => m.ConfirmEmailPage) },
  { path: 'reset-password', loadComponent: () => import('./pages/reset-password').then(m => m.ResetPasswordPage) },
  { path: 'techniques', component: TechniqueListPage },
  { path: 'r/:token', component: SharedRecipePage }, // public: shared recipe links
  { path: 'library', component: LibraryListPage }, // public: built-in recipes
  { path: 'library/:slug', component: LibraryDetailPage },
  // public; loaded on demand so the map code isn't in every page load
  { path: 'world', loadComponent: () => import('./pages/world-map').then(m => m.WorldMapPage) },

  { path: 'recipes', component: RecipeListPage, canActivate: [authGuard] },
  { path: 'recipes/new', component: RecipeFormPage, canActivate: [authGuard] },
  { path: 'recipes/import', loadComponent: () => import('./pages/import-recipe').then(m => m.ImportRecipePage), canActivate: [authGuard] },
  { path: 'recipes/:id', loadComponent: () => import('./pages/recipe-detail').then(m => m.RecipeDetailPage), canActivate: [authGuard] },
  { path: 'recipes/:id/edit', component: RecipeFormPage, canActivate: [authGuard] },
  { path: 'fridge', component: FridgePage, canActivate: [authGuard] },
  { path: 'shopping-list', component: GroceryListPage, canActivate: [authGuard] },
  { path: 'kitchen', loadComponent: () => import('./pages/kitchen').then(m => m.KitchenPage), canActivate: [authGuard] },
  { path: 'settings', loadComponent: () => import('./pages/settings').then(m => m.SettingsPage), canActivate: [authGuard] },
  { path: 'plan-week', loadComponent: () => import('./pages/plan-week').then(m => m.PlanWeekPage), canActivate: [authGuard] },
  // Cook mode works for your recipes and library recipes; it's full screen, so it loads on demand
  { path: 'recipes/:id/cook', loadComponent: () => import('./pages/cook-mode').then(m => m.CookModePage), canActivate: [authGuard] },
  { path: 'library/:slug/cook', loadComponent: () => import('./pages/cook-mode').then(m => m.CookModePage) },
  { path: 'meal-plans', component: MealPlanListPage, canActivate: [authGuard] },
  { path: 'meal-plans/:id', component: MealPlanDetailPage, canActivate: [authGuard] },
  { path: 'meal-plans/:id/shopping-list', component: ShoppingListPage, canActivate: [authGuard] },
  { path: 'meal-plans/:id/prep', loadComponent: () => import('./pages/prep-day').then(m => m.PrepDayPage), canActivate: [authGuard] },

  { path: '**', redirectTo: 'recipes' }
];
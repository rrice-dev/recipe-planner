import { Routes } from '@angular/router';
import { authGuard } from './auth.guard';
import { LoginPage } from './pages/login';
import { RegisterPage } from './pages/register';
import { RecipeListPage } from './pages/recipe-list';
import { RecipeDetailPage } from './pages/recipe-detail';
import { RecipeFormPage } from './pages/recipe-form';
import { TechniqueListPage } from './pages/technique-list';
import { MealPlanListPage } from './pages/meal-plan-list';
import { MealPlanDetailPage } from './pages/meal-plan-detail';
import { ShoppingListPage } from './pages/shopping-list';
import { FridgePage } from './pages/fridge';
import { SharedRecipePage } from './pages/shared-recipe';

export const routes: Routes = [
  { path: '', redirectTo: 'recipes', pathMatch: 'full' },
  { path: 'login', component: LoginPage },
  { path: 'register', component: RegisterPage },
  { path: 'techniques', component: TechniqueListPage },
  { path: 'r/:token', component: SharedRecipePage }, // public: shared recipe links

  { path: 'recipes', component: RecipeListPage, canActivate: [authGuard] },
  { path: 'recipes/new', component: RecipeFormPage, canActivate: [authGuard] },
  { path: 'recipes/:id', component: RecipeDetailPage, canActivate: [authGuard] },
  { path: 'recipes/:id/edit', component: RecipeFormPage, canActivate: [authGuard] },
  { path: 'fridge', component: FridgePage, canActivate: [authGuard] },
  { path: 'meal-plans', component: MealPlanListPage, canActivate: [authGuard] },
  { path: 'meal-plans/:id', component: MealPlanDetailPage, canActivate: [authGuard] },
  { path: 'meal-plans/:id/shopping-list', component: ShoppingListPage, canActivate: [authGuard] },

  { path: '**', redirectTo: 'recipes' }
];
import { Routes } from '@angular/router';
import { RecipeListPage } from './pages/recipe-list';
import { RecipeDetailPage } from './pages/recipe-detail';
import { RecipeFormPage } from './pages/recipe-form';
import { TechniqueListPage } from './pages/technique-list';
import { MealPlanListPage } from './pages/meal-plan-list';
import { MealPlanDetailPage } from './pages/meal-plan-detail';
import { ShoppingListPage } from './pages/shopping-list';

export const routes: Routes = [
  { path: '', redirectTo: 'recipes', pathMatch: 'full' },
  { path: 'recipes', component: RecipeListPage },
  { path: 'recipes/new', component: RecipeFormPage },
  { path: 'recipes/:id', component: RecipeDetailPage },
  { path: 'recipes/:id/edit', component: RecipeFormPage },
  { path: 'techniques', component: TechniqueListPage },
  { path: 'meal-plans', component: MealPlanListPage },
{ path: 'meal-plans/:id', component: MealPlanDetailPage },
{ path: 'meal-plans/:id/shopping-list', component: ShoppingListPage },
  { path: '**', redirectTo: 'recipes' }
];
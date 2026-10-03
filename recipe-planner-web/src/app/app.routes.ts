import { Routes } from '@angular/router';
import { RecipeListPage } from './pages/recipe-list';
import { RecipeDetailPage } from './pages/recipe-detail';
import { RecipeFormPage } from './pages/recipe-form';
import { TechniqueListPage } from './pages/technique-list';

export const routes: Routes = [
  { path: '', redirectTo: 'recipes', pathMatch: 'full' },
  { path: 'recipes', component: RecipeListPage },
  { path: 'recipes/new', component: RecipeFormPage },
  { path: 'recipes/:id', component: RecipeDetailPage },
  { path: 'recipes/:id/edit', component: RecipeFormPage },
  { path: 'techniques', component: TechniqueListPage },
  { path: '**', redirectTo: 'recipes' }
];
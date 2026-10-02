import { Routes } from '@angular/router';
import { RecipeListPage } from './pages/recipe-list';
import { RecipeDetailPage } from './pages/recipe-detail';
import { TechniqueListPage } from './pages/technique-list';

export const routes: Routes = [
  { path: '', redirectTo: 'recipes', pathMatch: 'full' },
  { path: 'recipes', component: RecipeListPage },
  { path: 'recipes/:id', component: RecipeDetailPage },
  { path: 'techniques', component: TechniqueListPage },
  { path: '**', redirectTo: 'recipes' }
];
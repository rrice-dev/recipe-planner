import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Ingredient, RecipeDetail, RecipeSummary, Technique } from './models';

export interface RecipeRequest {
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
}

export interface RecipeIngredientRequest {
  ingredientId: number;
  quantity: number;
  unit: string;
  note: string | null;
}

export interface StepRequest {
  instruction: string;
  techniqueId: number | null;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  getRecipes() {
    return this.http.get<RecipeSummary[]>('/api/recipes');
  }

  getRecipe(id: number) {
    return this.http.get<RecipeDetail>(`/api/recipes/${id}`);
  }

  createRecipe(body: RecipeRequest) {
    return this.http.post<RecipeSummary>('/api/recipes', body);
  }

  updateRecipe(id: number, body: RecipeRequest) {
    return this.http.put<void>(`/api/recipes/${id}`, body);
  }

  setRecipeIngredients(id: number, items: RecipeIngredientRequest[]) {
    return this.http.put<void>(`/api/recipes/${id}/ingredients`, items);
  }

  setRecipeSteps(id: number, steps: StepRequest[]) {
    return this.http.put<void>(`/api/recipes/${id}/steps`, steps);
  }

  linkTechnique(recipeId: number, techniqueId: number) {
    return this.http.post<void>(`/api/recipes/${recipeId}/techniques/${techniqueId}`, null);
  }

  getTechniques() {
    return this.http.get<Technique[]>('/api/techniques');
  }

  getIngredients() {
    return this.http.get<Ingredient[]>('/api/ingredients');
  }

  createIngredient(name: string, storeSection: string | null) {
    return this.http.post<Ingredient>('/api/ingredients', { name, storeSection });
  }
}
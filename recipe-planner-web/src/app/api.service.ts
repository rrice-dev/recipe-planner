import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FridgeResponse, RecipeDraft, ScanResponse, SharedRecipe, Ingredient, MealPlan, RecipeDetail, RecipeSummary, ShoppingList, Technique } from './models';

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

  getMealPlans() {
  return this.http.get<MealPlan[]>('/api/mealplans');
}

getMealPlan(id: number) {
  return this.http.get<MealPlan>(`/api/mealplans/${id}`);
}

createMealPlan(name: string, weekStart: string) {
  return this.http.post<MealPlan>('/api/mealplans', { name, weekStart });
}

deleteMealPlan(id: number) {
  return this.http.delete<void>(`/api/mealplans/${id}`);
}

setMealPlanEntries(id: number, entries: { day: string; recipeId: number; servings: number }[]) {
  return this.http.put<void>(`/api/mealplans/${id}/entries`, entries);
}

getShoppingList(id: number) {
  return this.http.get<ShoppingList>(`/api/mealplans/${id}/shopping-list`);
}

scanPhoto(photo: Blob) {
  // Raw image bytes, not a form upload; the API checks the type and contents itself
  return this.http.post<ScanResponse>('/api/fridge/scan', photo, { headers: { 'Content-Type': 'image/jpeg' } });
}

shareRecipe(id: number) {
  return this.http.post<{ shareToken: string }>(`/api/recipes/${id}/share`, null);
}

unshareRecipe(id: number) {
  return this.http.delete<void>(`/api/recipes/${id}/share`);
}

rateMyRecipe(id: number, stars: number) {
  return this.http.put<void>(`/api/recipes/${id}/rating`, { stars });
}

clearMyRecipeRating(id: number) {
  return this.http.delete<void>(`/api/recipes/${id}/rating`);
}

getSharedRecipe(token: string) {
  return this.http.get<SharedRecipe>(`/api/shared/${encodeURIComponent(token)}`);
}

rateSharedRecipe(token: string, stars: number) {
  return this.http.put<void>(`/api/shared/${encodeURIComponent(token)}/rating`, { stars });
}

clearSharedRating(token: string) {
  return this.http.delete<void>(`/api/shared/${encodeURIComponent(token)}/rating`);
}

getFridgeRecipe(dishName: string, ingredients: string[]) {
  return this.http.post<RecipeDraft>('/api/fridge/recipe', { dishName, ingredients });
}

getFridgeSuggestions(ingredients: string[]) {
  return this.http.post<FridgeResponse>('/api/fridge/suggestions', { ingredients });
}

}

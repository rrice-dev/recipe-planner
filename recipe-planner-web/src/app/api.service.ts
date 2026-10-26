import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { PlanCost, RecipeCost, NutritionTargets, PlanNutrition, GroceryAddResult, Leftover, LeftoverLocation, PantryItem, Collection, PrepSession, PlanSuggestResponse, CountryDishesResponse, LibraryDetail, LibrarySummary, FridgeResponse, KrogerLocation, KrogerMatch, KrogerStatus, GroceryAdd, GroceryItem, RecipeDraft, ScanResponse, SharedRecipe, Ingredient, MealPlan, RecipeDetail, RecipeSummary, ShoppingList, Technique } from './models';

export interface RecipeRequest {
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  sourceUrl?: string | null;
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

getKrogerStatus() {
  return this.http.get<KrogerStatus>('/api/kroger/status');
}

findKrogerStores(zip: string) {
  return this.http.get<KrogerLocation[]>('/api/kroger/locations', { params: { zip } });
}

setKrogerStore(locationId: string, name: string) {
  return this.http.put<void>('/api/kroger/store', { locationId, name });
}

matchKrogerProducts() {
  return this.http.post<KrogerMatch[]>('/api/kroger/match', null);
}

sendToKrogerCart(modality: 'PICKUP' | 'DELIVERY', items: { groceryItemId: number; upc: string; description: string; quantity: number }[]) {
  return this.http.post<{ added: number }>('/api/kroger/cart', { modality, items, markChecked: true });
}

disconnectKroger() {
  return this.http.delete<void>('/api/kroger/connection');
}

getGroceryList() {
  return this.http.get<GroceryItem[]>('/api/shopping-list');
}

addGroceryItems(items: GroceryAdd[]) {
  return this.http.post<GroceryItem[]>('/api/shopping-list/items', items);
}

addRecipeToGroceryList(recipeId: number, servings: number | null) {
  return this.http.post<GroceryAddResult>(`/api/shopping-list/from-recipe/${recipeId}`, { servings });
}

addMealPlanToGroceryList(planId: number) {
  return this.http.post<GroceryAddResult>(`/api/shopping-list/from-meal-plan/${planId}`, null);
}

updateGroceryItem(id: number, changes: { checked?: boolean; quantity?: number }) {
  return this.http.patch<void>(`/api/shopping-list/items/${id}`, changes);
}

removeGroceryItem(id: number) {
  return this.http.delete<void>(`/api/shopping-list/items/${id}`);
}

clearGroceryList(checkedOnly: boolean) {
  return this.http.delete<void>('/api/shopping-list', { params: { checkedOnly } });
}

uploadRecipePhoto(id: number, photo: Blob) {
  return this.http.put<{ photoVersion: string }>(`/api/recipes/${id}/photo`, photo, { headers: { 'Content-Type': 'image/jpeg' } });
}

removeRecipePhoto(id: number) {
  return this.http.delete<void>(`/api/recipes/${id}/photo`);
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

getLibrary() {
  return this.http.get<LibrarySummary[]>('/api/library');
}

getLibraryRecipe(slug: string) {
  return this.http.get<LibraryDetail>(`/api/library/${encodeURIComponent(slug)}`);
}

addLibraryRecipe(slug: string) {
  return this.http.post<{ recipeId: number }>(`/api/library/${encodeURIComponent(slug)}/add`, null);
}

getCountryDishes(country: string) {
  return this.http.post<CountryDishesResponse>('/api/world/dishes', { country });
}

getCountryRecipe(country: string, dishName: string) {
  return this.http.post<RecipeDraft>('/api/world/recipe', { country, dishName });
}

suggestMeals(body: { count: number; diet: string | null; tag: string | null; cuisine: string | null; source: string; exclude: string[]; useMyDiet: boolean }) {
  return this.http.post<PlanSuggestResponse>('/api/mealplans/suggest', body);
}

savePlannedWeek(name: string, weekStart: string, picks: { day: string; key: string; servings: number }[]) {
  return this.http.post<MealPlan>('/api/mealplans/plan', { name, weekStart, picks });
}

setFavorite(recipeId: number, isFavorite: boolean) {
  return this.http.put<void>(`/api/recipes/${recipeId}/favorite`, { isFavorite });
}

setNotes(recipeId: number, notes: string | null) {
  return this.http.put<void>(`/api/recipes/${recipeId}/notes`, { notes });
}

markCooked(recipeId: number, cookedOn: string | null = null) {
  return this.http.post<{ id: number; cookedOn: string }>(`/api/recipes/${recipeId}/cooked`, { cookedOn });
}

removeCooked(recipeId: number, entryId: number) {
  return this.http.delete<void>(`/api/recipes/${recipeId}/cooked/${entryId}`);
}

getCollections() {
  return this.http.get<Collection[]>('/api/collections');
}

createCollection(name: string) {
  return this.http.post<Collection>('/api/collections', { name });
}

renameCollection(id: number, name: string) {
  return this.http.put<void>(`/api/collections/${id}`, { name });
}

deleteCollection(id: number) {
  return this.http.delete<void>(`/api/collections/${id}`);
}

setRecipeCollections(recipeId: number, collectionIds: number[]) {
  return this.http.put<void>(`/api/recipes/${recipeId}/collections`, collectionIds);
}

importRecipe(url: string) {
  return this.http.post<{ recipe: RecipeDraft; sourceUrl: string; method: 'structured' | 'ai' }>('/api/import', { url });
}

getPrepSession(planId: number, prepDay: string | null) {
  return this.http.get<PrepSession>(`/api/mealplans/${planId}/prep`, { params: prepDay ? { prepDay } : {} });
}


getPantry() {
  return this.http.get<PantryItem[]>('/api/pantry');
}

addToPantry(names: string[]) {
  return this.http.post<PantryItem[]>('/api/pantry', { names });
}

removeFromPantry(id: number) {
  return this.http.delete<void>(`/api/pantry/${id}`);
}

getLeftovers() {
  return this.http.get<Leftover[]>('/api/leftovers');
}

addLeftover(leftover: { name: string | null; recipeId: number | null; portions: number; location: LeftoverLocation; madeOn: string | null }) {
  return this.http.post<Leftover>('/api/leftovers', leftover);
}

updateLeftover(id: number, changes: { portions?: number; location?: LeftoverLocation; on?: string }) {
  return this.http.patch<Leftover>(`/api/leftovers/${id}`, changes);
}

eatLeftover(id: number, portions = 1) {
  return this.http.post<Leftover | null>(`/api/leftovers/${id}/eat`, { portions });
}

removeLeftover(id: number) {
  return this.http.delete<void>(`/api/leftovers/${id}`);
}

getPlanNutrition(planId: number) {
  return this.http.get<PlanNutrition>(`/api/mealplans/${planId}/nutrition`);
}

getNutritionTargets() {
  return this.http.get<NutritionTargets>('/api/settings/nutrition-targets');
}

saveNutritionTargets(targets: NutritionTargets) {
  return this.http.put<NutritionTargets>('/api/settings/nutrition-targets', targets);
}

getRecipeCost(recipeId: number, servings: number | null) {
  return this.http.get<RecipeCost>(`/api/recipes/${recipeId}/cost`, { params: servings ? { servings } : {} });
}

getPlanCost(planId: number) {
  return this.http.get<PlanCost>(`/api/mealplans/${planId}/cost`);
}

getBudget() {
  return this.http.get<{ weeklyBudget: number | null }>('/api/settings/budget');
}

saveBudget(weeklyBudget: number | null) {
  return this.http.put<{ weeklyBudget: number | null }>('/api/settings/budget', { weeklyBudget });
}
}

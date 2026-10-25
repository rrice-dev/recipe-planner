export interface RecipeSummary {
  id: number;
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  photoVersion: string | null;
}

export interface TechniqueSummary {
  id: number;
  name: string;
  difficulty: number;
}

export interface RecipeIngredient {
  ingredientId: number;
  name: string;
  quantity: number;
  unit: string;
  note: string | null;
  storeSection: string | null;
}

export interface RecipeStep {
  order: number;
  instruction: string;
  techniqueId: number | null;
  techniqueName: string | null;
}

export interface RecipeSocial {
  shareToken: string | null;
  myRating: number | null;
  communityAverage: number | null;
  communityCount: number;
}

export interface RecipeDetail extends RecipeSummary {
  techniques: TechniqueSummary[];
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
  social: RecipeSocial;
}

export interface Technique {
  id: number;
  name: string;
  summary: string;
  donenessCues: string | null;
  commonMistakes: string | null;
  difficulty: number;
}
export interface Ingredient {
  id: number;
  name: string;
  storeSection: string | null;
}

export interface MealPlanEntry {
  day: string;
  recipeId: number;
  recipeTitle: string;
  servings: number;
}

export interface MealPlan {
  id: number;
  name: string;
  weekStart: string;
  entries: MealPlanEntry[];
}

export interface ShoppingListItem {
  ingredient: string;
  quantity: number;
  unit: string;
  storeSection: string | null;
}

export interface ShoppingList {
  mealPlanId: number;
  name: string;
  items: ShoppingListItem[];
}
export interface FridgeRecipeMatch {
  recipeId: number;
  title: string;
  have: string[];
  missing: string[];
}

export interface FridgeMealIdea {
  name: string;
  description: string;
  usesIngredients: string[];
  extraIngredients: string[];
  techniques: TechniqueSummary[];
}

export interface FridgeResponse {
  fromYourRecipes: FridgeRecipeMatch[];
  newIdeas: FridgeMealIdea[];
  aiAvailable: boolean;
  aiNotice: string | null;
}

export interface ScanResponse {
  ingredients: string[];
}

export interface RecipeDraftIngredient {
  name: string;
  quantity: number;
  unit: string;
  note: string | null;
  onHand: boolean;
}

export interface RecipeDraftStep {
  instruction: string;
  techniqueId: number | null;
  techniqueName: string | null;
}

export interface RecipeDraft {
  title: string;
  description: string;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  ingredients: RecipeDraftIngredient[];
  steps: RecipeDraftStep[];
}

export interface RatingSummary {
  average: number | null;
  count: number;
  myRating: number | null;
  isOwner: boolean;
}

export interface SharedRecipe {
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  techniques: string[];
  ingredients: { name: string; quantity: number; unit: string; note: string | null }[];
  steps: { order: number; instruction: string; techniqueName: string | null }[];
  rating: RatingSummary;
  photoVersion: string | null;
}

export interface GroceryItem {
  id: number;
  name: string;
  quantity: number | null;
  unit: string | null;
  storeSection: string | null;
  checked: boolean;
  sources: string | null;
}

export interface GroceryAdd {
  name: string;
  quantity: number | null;
  unit: string | null;
  source: string | null;
}

export interface KrogerStatus {
  available: boolean;
  connected: boolean;
  locationId: string | null;
  locationName: string | null;
}

export interface KrogerLocation {
  locationId: string;
  name: string;
  address: string;
}

export interface KrogerProductOption {
  upc: string;
  description: string;
  brand: string | null;
  size: string | null;
  price: number | null;
  promoPrice: number | null;
  imageUrl: string | null;
}

export interface KrogerMatch {
  groceryItemId: number;
  name: string;
  amount: string | null;
  options: KrogerProductOption[];
  selectedUpc: string | null;
}

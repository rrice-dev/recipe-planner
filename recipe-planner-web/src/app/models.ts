export interface RecipeSummary {
  id: number;
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  photoVersion: string | null;
  // Only on the list endpoint, for searching and filtering your book
  isFavorite?: boolean;
  timesCooked?: number;
  lastCooked?: string | null;
  collectionIds?: number[];
  ingredientNames?: string[];
  dietTags?: string[];
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
  personal: RecipePersonal | null;
  sourceUrl?: string | null;
}

export interface RecipePersonal {
  isFavorite: boolean;
  notes: string | null;
  cooked: { id: number; cookedOn: string }[];
  collectionIds: number[];
}

export interface Collection {
  id: number;
  name: string;
  recipeCount: number;
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
  inPantry: boolean;
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

export interface LibrarySummary {
  slug: string;
  title: string;
  description: string;
  cuisine: string;
  country: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  tags: string[];
  ingredientCount: number;
}

export interface LibraryDetail {
  slug: string;
  title: string;
  description: string;
  cuisine: string;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
  tags: string[];
  ingredients: { name: string; quantity: number; unit: string; note: string | null; storeSection: string | null }[];
  steps: { order: number; instruction: string; technique: string | null }[];
}

export interface CountryDish {
  name: string;
  description: string;
  aipFriendly: boolean;
}

export interface CountryDishesResponse {
  country: string;
  dishes: CountryDish[];
  aiAvailable: boolean;
  aiNotice: string | null;
}

export interface PlanCandidate {
  key: string;            // "lib:slug" or "mine:id"
  title: string;
  cuisine: string | null;
  totalMinutes: number;
  tags: string[];
  fromLibrary: boolean;
  recipeId: number | null;
  slug: string | null;
  photoVersion: string | null;
}

export interface PlanSuggestResponse {
  suggestions: PlanCandidate[];
  matching: number;
  diet: string | null;
}

export interface PrepSession {
  mealPlanId: number;
  name: string;
  prepDay: string;
  meals: { day: string; recipeId: number; title: string; servings: number; approach: 'CookAhead' | 'PrepAheadCookFresh' | 'Freeze'; reason: string }[];
  startFirst: { recipeId: number; title: string; minutes: number; firstStep: string }[];
  chopOnce: { ingredient: string; prep: string; quantity: number; unit: string; storeSection: string | null; forRecipes: string[] }[];
  marinate: string[];
}

export interface GroceryAddResult {
  items: GroceryItem[];
  skippedPantry: string[];
}

export interface PantryItem {
  id: number;
  name: string;
}

export type LeftoverLocation = 'fridge' | 'freezer';

export interface Leftover {
  id: number;
  name: string;
  recipeId: number | null;
  portions: number;
  location: LeftoverLocation;
  madeOn: string;
  useBy: string;
}

export interface Nutrients { calories: number; protein: number; carbs: number; fat: number; fiber: number; sugar: number; sodiumMg: number }

export interface NutritionTargets { calories: number | null; protein: number | null; carbs: number | null; fat: number | null }

export interface PlanNutrition {
  available: boolean;
  days: { day: string; meals: { recipeId: number; title: string; perServing: Nutrients | null; complete: boolean }[]; total: Nutrients }[];
  dailyAverage: Nutrients | null;
  targets: NutritionTargets;
  complete: boolean;
}

export type CostStatus = 'ok' | 'unavailable' | 'no-store';

export interface CostLine { ingredient: string; product: string | null; size: string | null; packagePrice: number | null; cost: number | null; approximate: boolean }

export interface RecipeCost {
  status: CostStatus;
  storeName: string | null;
  servings: number;
  total: number | null;
  perServing: number | null;
  counted: number;
  totalIngredients: number;
  lines: CostLine[];
}

export interface PlanCost {
  status: CostStatus;
  storeName: string | null;
  meals: { day: string; recipeId: number; title: string; servings: number; cost: number | null; perServing: number | null; complete: boolean }[];
  total: number | null;
  budget: number | null;
  complete: boolean;
}

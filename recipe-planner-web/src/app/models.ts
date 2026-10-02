export interface RecipeSummary {
  id: number;
  title: string;
  description: string | null;
  cuisine: string | null;
  servings: number;
  prepMinutes: number;
  cookMinutes: number;
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

export interface RecipeDetail extends RecipeSummary {
  techniques: TechniqueSummary[];
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
}

export interface Technique {
  id: number;
  name: string;
  summary: string;
  donenessCues: string | null;
  commonMistakes: string | null;
  difficulty: number;
}
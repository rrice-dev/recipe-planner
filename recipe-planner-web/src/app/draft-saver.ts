import { Injectable, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { RecipeDraft } from './models';

// Saves an AI-written recipe through the same endpoints the recipe editor uses, so all their validation applies.
// Used by "What Can I Make?" and the world map.
@Injectable({ providedIn: 'root' })
export class DraftSaver {
  private api = inject(ApiService);

  /** Returns the new recipe's id */
  async save(draft: RecipeDraft, cuisine: string | null = null, sourceUrl: string | null = null, fromCookbook = false): Promise<number> {
    // 1. Find or create each ingredient in the shared list
    const known = new Map((await firstValueFrom(this.api.getIngredients())).map(i => [i.name.toLowerCase(), i.id]));
    const items: { ingredientId: number; quantity: number; unit: string; note: string | null }[] = [];
    for (const i of draft.ingredients) {
      let id = known.get(i.name.toLowerCase());
      if (id === undefined) {
        try {
          id = (await firstValueFrom(this.api.createIngredient(i.name, null))).id;
        } catch (e) {
          // Someone else created it a moment ago: look it up instead
          if (!(e instanceof HttpErrorResponse && e.status === 409)) throw e;
          const fresh = await firstValueFrom(this.api.getIngredients());
          id = fresh.find(f => f.name.toLowerCase() === i.name.toLowerCase())?.id;
          if (id === undefined) throw e;
        }
        known.set(i.name.toLowerCase(), id);
      }
      if (!items.some(x => x.ingredientId === id)) {
        items.push({ ingredientId: id, quantity: i.quantity, unit: i.unit, note: i.note });
      }
    }

    // 2. The recipe itself, then its ingredients, steps and techniques
    const recipe = await firstValueFrom(this.api.createRecipe({
      title: draft.title,
      description: draft.description || null,
      cuisine,
      servings: draft.servings,
      prepMinutes: draft.prepMinutes,
      cookMinutes: draft.cookMinutes,
      sourceUrl,
      fromCookbook
    }));
    await firstValueFrom(this.api.setRecipeIngredients(recipe.id, items));
    await firstValueFrom(this.api.setRecipeSteps(recipe.id,
      draft.steps.map(s => ({ instruction: s.instruction, techniqueId: s.techniqueId }))));
    const techniqueIds = [...new Set(draft.steps.map(s => s.techniqueId).filter((id): id is number => id !== null))];
    for (const id of techniqueIds) {
      await firstValueFrom(this.api.linkTechnique(recipe.id, id));
    }
    return recipe.id;
  }
}

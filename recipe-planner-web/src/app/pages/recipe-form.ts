import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api.service';
import { Ingredient, RecipeIngredient, RecipeStep, Technique } from '../models';

@Component({
  selector: 'app-recipe-form',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <a [routerLink]="id() ? ['/recipes', id()] : ['/recipes']" class="back">← Cancel</a>
    <h1>{{ id() ? 'Edit recipe' : 'New recipe' }}</h1>

    <form [formGroup]="form" (ngSubmit)="save()" class="form">
      <label>Title
        <input formControlName="title" placeholder="e.g. Braised Short Ribs" />
      </label>
      @if (form.controls.title.touched && form.controls.title.invalid) {
        <p class="error">Title is required (200 characters max).</p>
      }

      <label>Description
        <textarea formControlName="description" rows="2"></textarea>
      </label>

      <div class="row">
        <label>Cuisine <input formControlName="cuisine" /></label>
        <label>Servings <input type="number" formControlName="servings" min="1" /></label>
        <label>Prep (min) <input type="number" formControlName="prepMinutes" min="0" /></label>
        <label>Cook (min) <input type="number" formControlName="cookMinutes" min="0" /></label>
      </div>
    </form>

    <h2>Ingredients</h2>
    @for (row of ingredientRows.controls; track row; let i = $index) {
      <div class="row item" [formGroup]="row">
        <select formControlName="ingredientId" class="grow">
          <option [ngValue]="null" disabled>Choose ingredient…</option>
          @for (ing of ingredients(); track ing.id) {
            <option [ngValue]="ing.id">{{ ing.name }}</option>
          }
        </select>
        <input type="number" formControlName="quantity" step="0.25" min="0" class="narrow" />
        <input formControlName="unit" placeholder="unit" class="narrow" />
        <input formControlName="note" placeholder="note (optional)" class="grow" />
        <button type="button" class="link" (click)="ingredientRows.removeAt(i)">Remove</button>
      </div>
    }
    <div class="row">
      <button type="button" (click)="ingredientRows.push(ingredientRow())">+ Add ingredient</button>
      <input #newIng placeholder="New ingredient name" class="grow" />
      <button type="button" (click)="addIngredient(newIng.value); newIng.value = ''">Create</button>
    </div>

    <h2>Steps</h2>
    @for (row of stepRows.controls; track row; let i = $index) {
      <div class="row item" [formGroup]="row">
        <span class="step-num">{{ i + 1 }}.</span>
        <textarea formControlName="instruction" rows="2" class="grow" placeholder="What to do"></textarea>
        <select formControlName="techniqueId">
          <option [ngValue]="null">No technique</option>
          @for (t of techniques(); track t.id) {
            <option [ngValue]="t.id">{{ t.name }}</option>
          }
        </select>
        <button type="button" class="link" (click)="stepRows.removeAt(i)">Remove</button>
      </div>
    }
    <button type="button" (click)="stepRows.push(stepRow())">+ Add step</button>

    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <div class="actions">
      <button type="button" class="primary" (click)="save()" [disabled]="saving()">
        {{ saving() ? 'Saving…' : 'Save recipe' }}
      </button>
    </div>
  `
})
export class RecipeFormPage implements OnInit {
  private api = inject(ApiService);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  id = input<string>();

  ingredients = signal<Ingredient[]>([]);
  techniques = signal<Technique[]>([]);
  saving = signal(false);
  error = signal<string | null>(null);

  form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: [''],
    cuisine: [''],
    servings: [4, [Validators.required, Validators.min(1)]],
    prepMinutes: [0, [Validators.required, Validators.min(0)]],
    cookMinutes: [0, [Validators.required, Validators.min(0)]]
  });

  ingredientRows = this.fb.array([this.ingredientRow()]);
  stepRows = this.fb.array([this.stepRow()]);

  ingredientRow(v?: RecipeIngredient) {
    return this.fb.group({
      ingredientId: this.fb.control<number | null>(v?.ingredientId ?? null, Validators.required),
      quantity: this.fb.nonNullable.control(v?.quantity ?? 1, [Validators.required, Validators.min(0.01)]),
      unit: this.fb.nonNullable.control(v?.unit ?? '', Validators.required),
      note: this.fb.nonNullable.control(v?.note ?? '')
    });
  }

  stepRow(v?: RecipeStep) {
    return this.fb.group({
      instruction: this.fb.nonNullable.control(v?.instruction ?? '', Validators.required),
      techniqueId: this.fb.control<number | null>(v?.techniqueId ?? null)
    });
  }

  async ngOnInit() {
    this.ingredients.set(await firstValueFrom(this.api.getIngredients()));
    this.techniques.set(await firstValueFrom(this.api.getTechniques()));

    const id = this.id();
    if (!id) return;

    const r = await firstValueFrom(this.api.getRecipe(Number(id)));
    this.form.patchValue({
      title: r.title,
      description: r.description ?? '',
      cuisine: r.cuisine ?? '',
      servings: r.servings,
      prepMinutes: r.prepMinutes,
      cookMinutes: r.cookMinutes
    });

    this.ingredientRows.clear();
    r.ingredients.forEach(i => this.ingredientRows.push(this.ingredientRow(i)));
    if (!r.ingredients.length) this.ingredientRows.push(this.ingredientRow());

    this.stepRows.clear();
    r.steps.forEach(s => this.stepRows.push(this.stepRow(s)));
    if (!r.steps.length) this.stepRows.push(this.stepRow());
  }

  async addIngredient(name: string) {
    const trimmed = name.trim();
    if (!trimmed) return;
    try {
      const created = await firstValueFrom(this.api.createIngredient(trimmed, null));
      this.ingredients.update(list => [...list, created].sort((a, b) => a.name.localeCompare(b.name)));
    } catch {
      this.error.set(`Couldn't create "${trimmed}". It may already exist.`);
    }
  }

  async save() {
    this.form.markAllAsTouched();
    this.ingredientRows.markAllAsTouched();
    this.stepRows.markAllAsTouched();

    if (this.form.invalid || this.ingredientRows.invalid || this.stepRows.invalid) {
      this.error.set('Please fill in all required fields: title, each ingredient with a quantity and unit, and each step.');
      return;
    }

    const items = this.ingredientRows.getRawValue().map(r => ({
      ingredientId: r.ingredientId!,
      quantity: r.quantity,
      unit: r.unit.trim(),
      note: r.note.trim() || null
    }));
    if (new Set(items.map(i => i.ingredientId)).size !== items.length) {
      this.error.set('Each ingredient can only be listed once.');
      return;
    }

    const steps = this.stepRows.getRawValue().map(s => ({
      instruction: s.instruction.trim(),
      techniqueId: s.techniqueId
    }));

    const v = this.form.getRawValue();
    const body = {
      title: v.title.trim(),
      description: v.description.trim() || null,
      cuisine: v.cuisine.trim() || null,
      servings: v.servings,
      prepMinutes: v.prepMinutes,
      cookMinutes: v.cookMinutes
    };

    this.saving.set(true);
    this.error.set(null);
    try {
      let recipeId: number;
      if (this.id()) {
        recipeId = Number(this.id());
        await firstValueFrom(this.api.updateRecipe(recipeId, body));
      } else {
        recipeId = (await firstValueFrom(this.api.createRecipe(body))).id;
      }

      await firstValueFrom(this.api.setRecipeIngredients(recipeId, items));
      await firstValueFrom(this.api.setRecipeSteps(recipeId, steps));

      const techniqueIds = [...new Set(steps.map(s => s.techniqueId).filter((t): t is number => t !== null))];
      for (const t of techniqueIds) {
        await firstValueFrom(this.api.linkTechnique(recipeId, t));
      }

      this.router.navigate(['/recipes', recipeId]);
    } catch {
      this.error.set('Something went wrong saving the recipe. Check the API terminal for details.');
    } finally {
      this.saving.set(false);
    }
  }
}
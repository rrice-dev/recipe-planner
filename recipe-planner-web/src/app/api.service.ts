import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RecipeDetail, RecipeSummary, Technique } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);

  getRecipes() {
    return this.http.get<RecipeSummary[]>('/api/recipes');
  }

  getRecipe(id: number) {
    return this.http.get<RecipeDetail>(`/api/recipes/${id}`);
  }

  getTechniques() {
    return this.http.get<Technique[]>('/api/techniques');
  }
}
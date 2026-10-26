import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth.service';
import { SettingsService } from './settings.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  auth = inject(AuthService);
  private router = inject(Router);
  // Created here so your theme and settings load as soon as the app starts
  private settings = inject(SettingsService);
  menuOpen = signal(false);

  constructor() {
    this.auth.ensureLoaded();
  }

  // On phones the menu covers the page, so close it once you pick somewhere to go
  closeOnLink(event: Event) {
    if ((event.target as HTMLElement).closest('a')) this.menuOpen.set(false);
  }

  async logout() {
    this.menuOpen.set(false);
    await this.auth.logout();
    this.router.navigate(['/login']);
  }
}

import { Injectable, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

// Online/offline status, "new version ready", and wiping cached data on logout
@Injectable({ providedIn: 'root' })
export class OfflineService {
  private updates = inject(SwUpdate);

  readonly online = signal(typeof navigator === 'undefined' ? true : navigator.onLine);
  readonly updateReady = signal(false);

  constructor() {
    if (typeof window !== 'undefined') {
      window.addEventListener('online', () => this.online.set(true));
      window.addEventListener('offline', () => this.online.set(false));
    }
    // The app itself is cached, so a new deploy arrives in the background; offer a reload when it's ready
    if (this.updates.isEnabled) {
      this.updates.versionUpdates.subscribe(e => { if (e.type === 'VERSION_READY') this.updateReady.set(true); });
      // Check every 30 minutes for people who leave the tab open
      setInterval(() => void this.updates.checkForUpdate().catch(() => {}), 30 * 60 * 1000);
    }
  }

  reload() {
    location.reload();
  }

}

/** Removes cached recipes, plans and photos, so the next person on a shared device can't see them */
export async function clearCachedPersonalData() {
  try {
    if (typeof caches === 'undefined') return;
    const names = await caches.keys();
    // The service worker keeps API responses in its "data" caches; the app files themselves are fine to keep
    await Promise.all(names.filter(n => n.includes(':data:')).map(n => caches.delete(n)));
  } catch {
    // Storage can be blocked (private browsing); nothing was cached then either
  }
}

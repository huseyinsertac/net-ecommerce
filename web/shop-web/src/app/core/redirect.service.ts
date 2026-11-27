import { Injectable } from '@angular/core';

/** Full-page navigation (to the payment provider). A service so specs can spy on it. */
@Injectable({ providedIn: 'root' })
export class RedirectService {
  to(url: string): void {
    window.location.assign(url);
  }
}

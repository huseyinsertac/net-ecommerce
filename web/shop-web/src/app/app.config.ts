import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { BasketService } from './core/basket/basket.service';
import { problemInterceptor } from './core/problem-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor, problemInterceptor])),
    // Created before the auth initializer so it hears a session restored on reload (and merges the
    // anonymous cart); not awaited, so a slow basket never blocks startup.
    provideAppInitializer(() => {
      void inject(BasketService).load();
    }),
    // One silent refresh restores the session after a reload; never rejects, so it cannot block startup.
    provideAppInitializer(() => inject(AuthService).initialize()),
  ],
};

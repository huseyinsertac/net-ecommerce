import { HttpClient } from '@angular/common/http';
import { Injectable, Signal, inject, signal } from '@angular/core';

export interface SystemInfo {
  name: string;
  version: string;
  environment: string;
}

@Injectable({ providedIn: 'root' })
export class SystemInfoService {
  private readonly _info = signal<SystemInfo | undefined>(undefined);
  private readonly _failed = signal(false);

  readonly info: Signal<SystemInfo | undefined> = this._info.asReadonly();
  /** True when the API could not be reached or returned an error. */
  readonly offline: Signal<boolean> = this._failed.asReadonly();

  constructor() {
    inject(HttpClient)
      .get<SystemInfo>('/api/system/info')
      .subscribe({
        next: (info) => this._info.set(info),
        error: () => this._failed.set(true),
      });
  }
}

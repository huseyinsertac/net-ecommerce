import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import type { AdminImageKey } from '../admin-api.service';

export const MAX_IMAGES = 4;

/** Pick 1 to 4 catalogue images; the order of selection is the display order. */
@Component({
  selector: 'app-image-picker',
  template: `
    <div class="grid" role="group" aria-label="Product images">
      @for (img of options(); track img.key) {
        @let n = order().get(img.key);
        <button
          type="button"
          class="tile"
          [class.selected]="n !== undefined"
          [attr.aria-pressed]="n !== undefined"
          [attr.aria-label]="img.key"
          [disabled]="n === undefined && selected().length >= max"
          (click)="toggle(img.key)"
        >
          <img [src]="img.thumbnailUrl" alt="" loading="lazy" />
          @if (n !== undefined) {
            <span class="badge">{{ n }}</span>
          }
        </button>
      }
    </div>
    <p class="hint" [class.invalid]="selected().length === 0">
      Choose 1 to {{ max }} images. The first is the main image. {{ selected().length }} selected.
    </p>
  `,
  styles: `
    :host {
      display: block;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(76px, 1fr));
      gap: 8px;
    }
    .tile {
      position: relative;
      aspect-ratio: 1;
      padding: 0;
      overflow: hidden;
      border: 2px solid var(--mat-sys-outline-variant);
      border-radius: 12px;
      background: var(--mat-sys-surface-container);
      cursor: pointer;

      &:disabled {
        opacity: 0.45;
        cursor: default;
      }
      &.selected {
        border-color: var(--mat-sys-primary);
      }
      &:focus-visible {
        outline: 2px solid var(--mat-sys-primary);
        outline-offset: 2px;
      }
    }
    img {
      display: block;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .badge {
      position: absolute;
      top: 4px;
      left: 4px;
      display: grid;
      place-items: center;
      width: 22px;
      height: 22px;
      border-radius: 50%;
      background: var(--mat-sys-primary);
      color: var(--mat-sys-on-primary);
      font: var(--mat-sys-label-medium);
    }
    .hint {
      margin: 8px 0 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);

      &.invalid {
        color: var(--mat-sys-error);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImagePickerComponent {
  readonly options = input.required<readonly AdminImageKey[]>();
  /** Selected keys in display order. */
  readonly selected = model<string[]>([]);
  protected readonly max = MAX_IMAGES;
  protected readonly order = computed(
    () => new Map(this.selected().map((key, i) => [key, i + 1] as const)),
  );

  protected toggle(key: string): void {
    const current = this.selected();
    if (current.includes(key)) this.selected.set(current.filter((k) => k !== key));
    else if (current.length < MAX_IMAGES) this.selected.set([...current, key]);
  }
}

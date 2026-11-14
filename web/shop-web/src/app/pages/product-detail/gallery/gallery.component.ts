import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';
import type { ProductImage } from '../../../api/catalog-api.service';

/** Main image with a thumbnail row. Left/Right arrows (when focused) step through, wrapping. */
@Component({
  selector: 'app-gallery',
  templateUrl: './gallery.component.html',
  styleUrl: './gallery.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GalleryComponent {
  readonly images = input.required<ProductImage[]>();
  readonly name = input.required<string>();

  /** Resets to the first image whenever the image list changes. */
  protected readonly index = linkedSignal<ProductImage[], number>({
    source: this.images,
    computation: () => 0,
  });
  protected readonly current = computed(() => this.images()[this.index()]);

  protected select(i: number): void {
    this.index.set(i);
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.images().length;
    if (count < 2) return;
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    this.index.update((i) => (i + step + count) % count);
  }
}

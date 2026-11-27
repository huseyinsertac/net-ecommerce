import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { Router, RouterLink } from '@angular/router';
import { Subscription, forkJoin, merge } from 'rxjs';
import { AdminApi, AdminCategory, AdminImageKey } from '../admin-api.service';
import { ImagePickerComponent } from '../image-picker/image-picker.component';
import { problemText, readProblem } from '../problem';
import { SpecFieldComponent } from '../spec-field/spec-field.component';
import { SpecForm, buildSpecForm, readSpecs } from '../spec-field/spec-form';

const MAX_AXES = 2;

interface AxisInput {
  name: string;
  values: string[];
}

/** What a generated variant row stands for: its label and its options keyed by axis name. */
interface VariantMeta {
  key: string;
  label: string;
  options: Record<string, string>;
}

type VariantGroup = FormGroup<{
  sku: FormControl<string>;
  price: FormControl<number | null>;
  stock: FormControl<number | null>;
}>;

/** `BRAND-NAME-VALUES`, upper-cased, anything but letters and digits collapsed to a single dash. */
export function suggestSku(brand: string, name: string, values: readonly string[]): string {
  return [brand, name, ...values]
    .join('-')
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

/** Splits a comma-separated list: trimmed, empty entries dropped, repeats removed. */
export function splitValues(text: string): string[] {
  return [...new Set(text.split(',').map((v) => v.trim()).filter((v) => v.length > 0))];
}

/** `/admin/products/new`: category, details, images, specs, option axes and the generated variant grid. */
@Component({
  selector: 'app-admin-product-create-page',
  imports: [
    ImagePickerComponent,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    ReactiveFormsModule,
    RouterLink,
    SpecFieldComponent,
  ],
  templateUrl: './admin-product-create.page.html',
  styleUrls: ['../../shared/order-view.scss', '../product-form.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminProductCreatePage implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);

  protected readonly view = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly categories = signal<AdminCategory[]>([]);
  protected readonly imageOptions = signal<AdminImageKey[]>([]);
  protected readonly images = signal<string[]>([]);
  protected readonly specs = signal<SpecForm | null>(null);
  protected readonly specSchema = computed(() => this.selectedCategory()?.specSchema ?? []);
  protected readonly variantMeta = signal<VariantMeta[]>([]);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly maxAxes = MAX_AXES;
  protected readonly skeleton = [1, 2, 3];

  protected readonly form = new FormGroup({
    category: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    brand: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    isFeatured: new FormControl(false, { nonNullable: true }),
  });
  protected readonly axes = new FormArray<FormGroup<{ name: FormControl<string>; values: FormControl<string> }>>([]);
  protected readonly variants = new FormArray<VariantGroup>([]);

  private readonly selectedCategory = signal<AdminCategory | null>(null);
  private sub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    this.form.controls.category.valueChanges.pipe(takeUntilDestroyed()).subscribe((slug) => {
      const category = this.categories().find((c) => c.slug === slug) ?? null;
      this.selectedCategory.set(category);
      this.specs.set(category ? buildSpecForm(category.specSchema) : null);
    });
    merge(this.axes.valueChanges, this.form.controls.brand.valueChanges, this.form.controls.name.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.rebuildVariants());
    this.rebuildVariants();
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.view.set('loading');
    this.sub?.unsubscribe();
    this.sub = forkJoin({ categories: this.api.categories(), images: this.api.imageKeys() }).subscribe({
      next: ({ categories, images }) => {
        this.categories.set(categories);
        this.imageOptions.set(images);
        this.view.set('ready');
      },
      error: () => this.view.set('error'),
    });
  }

  protected addAxis(): void {
    if (this.axes.length >= MAX_AXES) return;
    this.axes.push(
      new FormGroup({
        name: new FormControl('', { nonNullable: true }),
        values: new FormControl('', { nonNullable: true }),
      }),
    );
  }

  protected removeAxis(index: number): void {
    this.axes.removeAt(index);
  }

  protected submit(): void {
    if (this.saving()) return;
    const specs = this.specs();
    this.form.markAllAsTouched();
    specs?.markAllAsTouched();
    this.variants.markAllAsTouched();
    const problem = this.firstProblem();
    if (problem || !specs) {
      this.formError.set(problem ?? 'Choose a category first.');
      return;
    }
    const v = this.form.getRawValue();
    const axes = this.parsedAxes();
    const meta = this.variantMeta();
    this.saving.set(true);
    this.formError.set(null);
    this.sub?.unsubscribe();
    this.sub = this.api
      .createProduct({
        categorySlug: v.category,
        name: v.name.trim(),
        brand: v.brand.trim(),
        description: v.description.trim(),
        isFeatured: v.isFeatured,
        imageKeys: this.images(),
        specs: readSpecs(this.specSchema(), specs),
        optionAxes: axes.map((a) => ({ name: a.name, values: a.values })),
        variants: this.variants.controls.map((group, i) => {
          const row = group.getRawValue();
          return { sku: row.sku.trim(), options: meta[i].options, price: row.price, stockOnHand: row.stock };
        }),
      })
      .subscribe({
        next: (created) => void this.router.navigate(['/admin/products', created.id]),
        error: (err: unknown) => {
          this.saving.set(false);
          this.formError.set(problemText(readProblem(err)));
        },
      });
  }

  /** The first reason the form cannot be sent yet, in reading order, or null when it can. */
  private firstProblem(): string | null {
    if (this.form.invalid || this.specs()?.invalid) return 'Fix the highlighted fields before creating.';
    if (this.images().length === 0) return 'Choose at least one image.';
    const half = this.axes.controls.some((g) => {
      const { name, values } = g.getRawValue();
      return (name.trim() === '') !== (splitValues(values).length === 0);
    });
    if (half) return 'Each option needs a name and at least one value.';
    if (this.variants.invalid) return 'Every variant needs a SKU, a price and a stock level.';
    const skus = this.variants.controls.map((g) => g.controls.sku.value.trim().toUpperCase());
    if (new Set(skus).size !== skus.length) return 'Each variant needs its own SKU.';
    return null;
  }

  /** Axes that are complete (a name and values); repeated names are ignored. */
  private parsedAxes(): AxisInput[] {
    const seen = new Set<string>();
    const result: AxisInput[] = [];
    for (const group of this.axes.controls) {
      const { name, values } = group.getRawValue();
      const parsed = { name: name.trim(), values: splitValues(values) };
      if (!parsed.name || parsed.values.length === 0 || seen.has(parsed.name.toLowerCase())) continue;
      seen.add(parsed.name.toLowerCase());
      result.push(parsed);
    }
    return result;
  }

  /** Rebuilds the variant grid as the cross product of the axes, keeping what was typed for rows that survive. */
  private rebuildVariants(): void {
    const axes = this.parsedAxes();
    const combos = axes.reduce<string[][]>(
      (acc, axis) => acc.flatMap((prefix) => axis.values.map((value) => [...prefix, value])),
      [[]],
    );
    const previous = new Map(this.variantMeta().map((m, i) => [m.key, this.variants.at(i)] as const));
    const { brand, name } = this.form.getRawValue();
    const meta: VariantMeta[] = [];
    const groups: VariantGroup[] = [];
    for (const combo of combos) {
      const key = combo.join('\u0000');
      const sku = suggestSku(brand, name, combo);
      let group = previous.get(key);
      if (group) {
        if (!group.controls.sku.dirty) group.controls.sku.setValue(sku, { emitEvent: false });
      } else {
        group = new FormGroup({
          sku: new FormControl(sku, { nonNullable: true, validators: [Validators.required, Validators.maxLength(64)] }),
          price: new FormControl<number | null>(null, [Validators.required, Validators.min(0.01), Validators.max(100000)]),
          stock: new FormControl<number | null>(null, [
            Validators.required,
            Validators.min(0),
            Validators.max(100000),
            Validators.pattern(/^\d+$/),
          ]),
        });
      }
      groups.push(group);
      meta.push({
        key,
        label: combo.length > 0 ? combo.join(' / ') : 'Default',
        options: Object.fromEntries(axes.map((axis, i) => [axis.name, combo[i]])),
      });
    }
    this.variants.clear({ emitEvent: false });
    groups.forEach((g) => this.variants.push(g, { emitEvent: false }));
    this.variantMeta.set(meta);
  }
}

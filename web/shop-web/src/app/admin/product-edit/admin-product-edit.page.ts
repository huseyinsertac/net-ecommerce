import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { Subscription, forkJoin } from 'rxjs';
import {
  AdminApi,
  AdminImageKey,
  AdminProductDto,
  AdminProductStatus,
} from '../admin-api.service';
import { ImagePickerComponent } from '../image-picker/image-picker.component';
import { ProductStatusChipComponent } from '../product-status-chip/product-status-chip.component';
import { problemText, readProblem } from '../problem';
import { SpecFieldComponent } from '../spec-field/spec-field.component';
import { SpecForm, buildSpecForm, readSpecs } from '../spec-field/spec-form';

const STATUSES: AdminProductStatus[] = ['Draft', 'Active', 'Archived'];

type VariantForm = FormGroup<{ price: FormControl<number | null>; stock: FormControl<number | null> }>;

/** `/admin/products/:id`: edit one product's details and specs, and each variant's price and stock. */
@Component({
  selector: 'app-admin-product-edit-page',
  imports: [
    ImagePickerComponent,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    ProductStatusChipComponent,
    ReactiveFormsModule,
    RouterLink,
    SpecFieldComponent,
  ],
  templateUrl: './admin-product-edit.page.html',
  styleUrls: ['../../shared/order-view.scss', '../product-form.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminProductEditPage implements OnInit {
  private readonly api = inject(AdminApi);

  /** Bound from the route. */
  readonly id = input.required<string>();

  protected readonly statuses = STATUSES;
  protected readonly view = signal<'loading' | 'ready' | 'notFound' | 'error'>('loading');
  protected readonly product = signal<AdminProductDto | null>(null);
  protected readonly imageOptions = signal<AdminImageKey[]>([]);
  protected readonly images = signal<string[]>([]);
  protected readonly specForm = signal<SpecForm | null>(null);
  protected readonly variantForms = signal<Record<string, VariantForm>>({});
  /** The one save in flight ('details' or a variant id); every Save button waits on it so versions never race. */
  protected readonly pending = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly rowErrors = signal<Record<string, string>>({});
  /** Set when someone else saved first: the page offers Reload instead of retrying. */
  protected readonly conflict = signal<string | null>(null);
  protected readonly skeleton = [1, 2, 3];

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    brand: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    isFeatured: new FormControl(false, { nonNullable: true }),
    status: new FormControl<AdminProductStatus>('Draft', { nonNullable: true }),
  });

  private subs: Subscription[] = [];

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subs.forEach((s) => s.unsubscribe()));
  }

  ngOnInit(): void {
    this.load(false);
  }

  /** Fetches the product; `silent` refreshes without flashing the skeleton (used by Reload). */
  protected load(silent: boolean): void {
    if (!silent) this.view.set('loading');
    this.subs.push(
      forkJoin({ product: this.api.product(this.id()), images: this.api.imageKeys() }).subscribe({
        next: ({ product, images }) => {
          this.imageOptions.set(images);
          this.apply(product, 'all');
          this.conflict.set(null);
          this.formError.set(null);
          this.rowErrors.set({});
          this.view.set('ready');
        },
        error: (err: unknown) =>
          this.view.set(err instanceof HttpErrorResponse && err.status === 404 ? 'notFound' : 'error'),
      }),
    );
  }

  protected save(): void {
    const specForm = this.specForm();
    if (this.pending() !== null || !specForm) return;
    this.form.markAllAsTouched();
    specForm.markAllAsTouched();
    if (this.form.invalid || specForm.invalid || this.images().length === 0) {
      this.formError.set('Fix the highlighted fields before saving.');
      return;
    }
    const product = this.product()!;
    const v = this.form.getRawValue();
    this.pending.set('details');
    this.formError.set(null);
    this.subs.push(
      this.api
        .updateProduct(product.id, {
          name: v.name.trim(),
          brand: v.brand.trim(),
          description: v.description.trim(),
          isFeatured: v.isFeatured,
          imageKeys: this.images(),
          specs: readSpecs(product.specSchema, specForm),
          status: v.status,
          version: product.version,
        })
        .subscribe({
          next: (dto) => {
            this.pending.set(null);
            this.formError.set(null);
            this.apply(dto, 'details');
          },
          error: (err: unknown) => {
            this.pending.set(null);
            this.fail(err, (text) => this.formError.set(text));
          },
        }),
    );
  }

  protected saveVariant(variantId: string): void {
    const form = this.variantForms()[variantId];
    if (this.pending() !== null || !form) return;
    form.markAllAsTouched();
    if (form.invalid) return;
    const product = this.product()!;
    const { price, stock } = form.getRawValue();
    this.pending.set(variantId);
    this.setRowError(variantId, null);
    this.subs.push(
      this.api
        .updateVariant(product.id, variantId, { price, stockOnHand: stock, version: product.version })
        .subscribe({
          next: (dto) => {
            this.pending.set(null);
            this.apply(dto, variantId);
          },
          error: (err: unknown) => {
            this.pending.set(null);
            this.fail(err, (text) => this.setRowError(variantId, text));
          },
        }),
    );
  }

  protected optionsLabel(product: AdminProductDto, options: Record<string, string>): string {
    const parts = product.optionAxes
      .filter((axis) => options[axis.key] !== undefined)
      .map((axis) => `${axis.label}: ${options[axis.key]}`);
    return parts.length > 0 ? parts.join(', ') : 'Single option';
  }

  /** A 409 version clash gets the Reload banner; everything else goes where the caller says. */
  private fail(err: unknown, show: (text: string) => void): void {
    const problem = readProblem(err);
    if (problem.code === 'catalog.concurrent_update') this.conflict.set(problem.title);
    else show(problemText(problem));
  }

  private setRowError(variantId: string, text: string | null): void {
    this.rowErrors.update((errors) => {
      const next = { ...errors };
      if (text) next[variantId] = text;
      else delete next[variantId];
      return next;
    });
  }

  /**
   * Adopts the returned DTO. Its version and server-owned data (reserved counts, slug, status) are always taken;
   * the saved scope ('details' or a variant id) and any form the admin has not touched are reset from it.
   * Forms with unsaved edits in other scopes are left alone.
   */
  private apply(dto: AdminProductDto, scope: 'all' | 'details' | string): void {
    const previous = this.product();
    const detailsPristine =
      !previous ||
      (this.form.pristine &&
        (this.specForm()?.pristine ?? true) &&
        this.images().join('|') === previous.imageKeys.join('|'));
    this.product.set(dto);
    if (scope === 'all' || scope === 'details' || detailsPristine) {
      this.form.reset({
        name: dto.name,
        brand: dto.brand,
        description: dto.description,
        isFeatured: dto.isFeatured,
        status: dto.status,
      });
      this.images.set([...dto.imageKeys]);
      this.specForm.set(buildSpecForm(dto.specSchema, dto.specs));
    }
    const forms = scope === 'all' ? {} : this.variantForms();
    this.variantForms.set(
      Object.fromEntries(
        dto.variants.map((variant) => {
          const existing = forms[variant.id];
          if (existing && existing.dirty && scope !== variant.id) return [variant.id, existing];
          if (existing) {
            existing.reset({ price: variant.price, stock: variant.stockOnHand });
            return [variant.id, existing];
          }
          return [variant.id, this.newVariantForm(variant.price, variant.stockOnHand)];
        }),
      ),
    );
  }

  private newVariantForm(price: number, stock: number): VariantForm {
    return new FormGroup({
      price: new FormControl<number | null>(price, [
        Validators.required,
        Validators.min(0.01),
        Validators.max(100000),
      ]),
      stock: new FormControl<number | null>(stock, [
        Validators.required,
        Validators.min(0),
        Validators.max(100000),
        Validators.pattern(/^\d+$/),
      ]),
    });
  }
}

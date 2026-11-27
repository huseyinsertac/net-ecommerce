import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import type { AdminSpecDefinition } from '../admin-api.service';
import type { SpecValue } from './spec-form';

/** One product spec input, rendered from its schema definition (number, text, bool or enum). */
@Component({
  selector: 'app-spec-field',
  imports: [
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    ReactiveFormsModule,
  ],
  template: `
    @let d = def();
    @if (d.type === 'bool') {
      <mat-slide-toggle class="spec-toggle" [formControl]="control()">{{ d.label }}</mat-slide-toggle>
    } @else {
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ d.label }}</mat-label>
        @switch (d.type) {
          @case ('number') {
            <input matInput type="number" step="any" [formControl]="control()" [required]="d.required" />
            @if (d.unit) {
              <span matTextSuffix>{{ d.unit }}</span>
            }
          }
          @case ('enum') {
            <mat-select [formControl]="control()" [required]="d.required">
              @if (!d.required) {
                <mat-option [value]="null">None</mat-option>
              }
              @for (v of d.allowedValues ?? []; track v) {
                <mat-option [value]="v">{{ v }}</mat-option>
              }
            </mat-select>
          }
          @default {
            <input matInput type="text" [formControl]="control()" [required]="d.required" />
          }
        }
        @if (control().hasError('required') && control().touched) {
          <mat-error>{{ d.label }} is required.</mat-error>
        }
      </mat-form-field>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    mat-form-field {
      width: 100%;
    }
    .spec-toggle {
      display: flex;
      align-items: center;
      min-height: 56px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SpecFieldComponent {
  readonly def = input.required<AdminSpecDefinition>();
  readonly control = input.required<FormControl<SpecValue | null>>();
}

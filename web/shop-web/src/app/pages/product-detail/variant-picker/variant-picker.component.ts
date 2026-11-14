import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import type { Variant } from '../../../api/catalog-api.service';
import { Selection, bestSelectionFor, isValueSelectable } from '../variant-logic';

interface Axis {
  key: string;
  label: string;
  values: string[];
}

/** One toggle group per option axis. Missing combinations are disabled; out-of-stock ones are not. */
@Component({
  selector: 'app-variant-picker',
  imports: [MatButtonToggleModule],
  templateUrl: './variant-picker.component.html',
  styleUrl: './variant-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VariantPickerComponent {
  readonly axes = input.required<Axis[]>();
  readonly variants = input.required<Variant[]>();
  readonly selection = input.required<Selection>();
  readonly selectionChange = output<Selection>();

  /** Safeguard: option values come from the variants, so this only disables a value no variant has. */
  protected selectable(axisKey: string, value: string): boolean {
    // Only values that appear in no variant at all are disabled. A value whose combination with the
    // other current picks is missing stays clickable: bestSelectionFor then adjusts the other axes.
    return isValueSelectable(this.variants(), {}, axisKey, value);
  }

  protected pick(axisKey: string, value: string): void {
    this.selectionChange.emit(bestSelectionFor(this.variants(), this.selection(), axisKey, value));
  }
}

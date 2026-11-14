import type { ProductDetail, Variant } from '../../api/catalog-api.service';

/** Chosen value per option axis key, e.g. `{ color: 'Black', storage: '256 GB' }`. */
export type Selection = Record<string, string>;

/** First variant with stock, else the first variant. */
export function initialSelection(detail: ProductDetail): Selection {
  const first = detail.variants.find((v) => v.available > 0) ?? detail.variants[0];
  return first ? { ...first.options } : {};
}

/** The variant matching every selected axis value, if one exists. */
export function resolveVariant(variants: Variant[], selection: Selection): Variant | undefined {
  const keys = Object.keys(selection);
  return variants.find((v) => keys.every((k) => v.options[k] === selection[k]));
}

/** True when a variant exists that has this value and the other currently selected values. */
export function isValueSelectable(
  variants: Variant[],
  selection: Selection,
  axisKey: string,
  value: string,
): boolean {
  return resolveVariant(variants, { ...selection, [axisKey]: value }) !== undefined;
}

/**
 * Switches one axis to `value`. If that exact combination is missing, picks the existing variant
 * with this value that shares the most other selected values (in-stock and earlier ones win ties).
 */
export function bestSelectionFor(
  variants: Variant[],
  selection: Selection,
  axisKey: string,
  value: string,
): Selection {
  const exact = resolveVariant(variants, { ...selection, [axisKey]: value });
  if (exact) return { ...exact.options };

  let best: Variant | undefined;
  let bestScore = -1;
  for (const v of variants) {
    if (v.options[axisKey] !== value) continue;
    const matches = Object.keys(selection).filter(
      (k) => k !== axisKey && v.options[k] === selection[k],
    ).length;
    const score = matches * 2 + (v.available > 0 ? 1 : 0);
    if (score > bestScore) {
      best = v;
      bestScore = score;
    }
  }
  return best ? { ...best.options } : { ...selection, [axisKey]: value };
}

import type { AdminSpecDefinition } from '../admin-api.service';
import { buildSpecForm, readSpecs } from './spec-form';

const schema: AdminSpecDefinition[] = [
  { key: 'screen', label: 'Screen', type: 'number', unit: 'in', required: true, allowedValues: null },
  { key: 'chip', label: 'Chip', type: 'text', unit: null, required: false, allowedValues: null },
  { key: 'esim', label: 'eSIM', type: 'bool', unit: null, required: true, allowedValues: null },
];

describe('spec form', () => {
  it('requires required non-toggle fields and defaults toggles to false', () => {
    const form = buildSpecForm(schema);
    expect(form.controls['screen'].valid).toBe(false);
    expect(form.controls['chip'].valid).toBe(true);
    expect(form.controls['esim'].value).toBe(false);
    expect(form.controls['esim'].valid).toBe(true);
  });

  it('reads back typed values and leaves blank optional ones out', () => {
    const form = buildSpecForm(schema, { screen: 6.1, esim: true });
    expect(readSpecs(schema, form)).toEqual({ screen: 6.1, esim: true });
    form.controls['chip'].setValue('  V1 ');
    expect(readSpecs(schema, form)).toEqual({ screen: 6.1, chip: 'V1', esim: true });
  });
});

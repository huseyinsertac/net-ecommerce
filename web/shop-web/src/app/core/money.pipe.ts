import { Pipe, PipeTransform } from '@angular/core';

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: number): string {
    return usd.format(value);
  }
}

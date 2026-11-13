import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BRAND } from '../../brand';
import { SystemInfoService } from '../../core/system-info.service';

@Component({
  selector: 'app-footer',
  templateUrl: './footer.component.html',
  styleUrl: './footer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FooterComponent {
  protected readonly brand = BRAND;
  protected readonly year = new Date().getFullYear();
  protected readonly system = inject(SystemInfoService);
}

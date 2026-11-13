import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { BRAND } from '../../brand';

@Component({
  selector: 'app-home',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeComponent {
  protected readonly brand = BRAND;
  protected readonly categories = [
    { name: 'Phones', icon: 'smartphone' },
    { name: 'Laptops', icon: 'laptop_mac' },
    { name: 'Audio', icon: 'headphones' },
    { name: 'Wearables', icon: 'watch' },
    { name: 'Cameras', icon: 'photo_camera' },
    { name: 'Accessories', icon: 'cable' },
  ];
}

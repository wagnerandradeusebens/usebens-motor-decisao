import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  exact: boolean;
}

/**
 * Shell da aplicação no padrão Material: toolbar no topo + sidenav lateral com a
 * navegação. Segue a marca Usebens (azul/teal) definida em styles.scss.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly nav: NavItem[] = [
    { label: 'Políticas', icon: 'account_tree', link: '/politicas', exact: false },
    { label: 'Fontes', icon: 'cloud', link: '/fontes', exact: false },
    { label: 'Variáveis Globais', icon: 'functions', link: '/variaveis-globais', exact: false },
  ];
}

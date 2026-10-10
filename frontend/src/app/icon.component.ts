import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-icon', standalone: true,
  template: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path [attr.d]="paths[name] || paths['book']" /></svg>`,
  styles: [`:host{display:inline-flex;width:20px;height:20px;flex-shrink:0}svg{width:100%;height:100%}`]
})
export class IconComponent {
  @Input() name = 'book';
  paths: Record<string, string> = {
    logout: 'M9 4H4v16h5M9 12h12M17 8l4 4-4 4',
    shield: 'M12 2l9 4v6c0 5-4 8-9 10-5-2-9-5-9-10V6l9-4zm-4 10 3 3 5-6',
    eye: 'M2 12s3-7 10-7 10 7 10 7-3 7-10 7S2 12 2 12zm13 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    'eye-off': 'M3 3l18 18M10.6 10.6a2 2 0 0 0 2.8 2.8M7 6c-3 2-5 6-5 6s3 7 10 7c2 0 3.6-.5 5-1.4M11 5h1c7 0 10 7 10 7s-1 2-3 4',
    overview: 'M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z',
    book: 'M12 5c-2-2-6-3-10-2v16c4-1 8 0 10 2 2-2 6-3 10-2V3c-4-1-8 0-10 2zm0 0v16',
    users: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3a4 4 0 0 1 0 8M22 21v-2a4 4 0 0 0-3-3.87M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0',
    graduate: 'M2 9l10-5 10 5-10 5L2 9zm4 2v6c4 3 8 3 12 0v-6M22 9v7',
    chart: 'M4 3v18h18M9 16v-5M14 16V7M19 16V4',
    plus: 'M12 5v14M5 12h14',
    search: 'M21 21l-5.2-5.2M18 10.5a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0',
    arrow: 'M5 12h14M14 7l5 5-5 5',
    down: 'M6 9l6 6 6-6',
    calendar: 'M8 2v4M16 2v4M3 10h18M4 4h16a1 1 0 0 1 1 1v15a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zM7 14h3M14 14h3M7 17h3',
    clock: 'M12 8v4l3 2M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
    check: 'M5 12l4 4L19 6',
    circlecheck: 'M9 12l2 2 4-4M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',
    close: 'M6 6l12 12M6 18L18 6',
    download: 'M12 3v12M7 10l5 5 5-5M4 16v5h16v-5',
    edit: 'M15 5l4 4M3 21l5-1 13-13a2.83 2.83 0 0 0-4-4L4 16l-1 5z',
    trash: 'M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7',
    refresh: 'M20 7v5h-5M4 17v-5h5M6.1 6.1A8 8 0 0 1 20 12M4 12a8 8 0 0 0 13.9 5.9',
    alert: 'M12 9v4M12 17h.01M10 3L2 19a1 1 0 0 0 1 2h18a1 1 0 0 0 1-2L14 3a2 2 0 0 0-4 0',
    menu: 'M3 6h18M3 12h18M3 18h18',
    pin: 'M12 22s8-7.1 8-13a8 8 0 1 0-16 0c0 5.9 8 13 8 13zM15 9a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    sprout: 'M12 21v-9M12 16C6 16 3 12 3 6c6 0 9 3 9 9M12 12c0-6 3-9 9-9 0 6-3 9-9 9',
    folder: 'M3 7V4h6l2 3h10v13H3V7z',
    mail: 'M3 5h18v14H3V5zm0 0 9 7 9-7'
  };
}

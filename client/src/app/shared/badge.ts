import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { ClaimStatus, PolicyStatus, PolicyType } from '../core/models';

type BadgeValue = ClaimStatus | PolicyStatus | PolicyType | string;

/**
 * A coloured label for a status or type.
 *
 * The colour is chosen by a lookup rather than by hashing the string, so a status the API adds later
 * renders in the neutral default instead of inheriting whatever colour a hash happened to produce.
 */
@Component({
  selector: 'app-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge ' + tone()">{{ value() }}</span>`,
  styles: `
    .badge {
      display: inline-block;
      padding: 0.0625rem 0.5rem;
      border-radius: 999px;
      font-size: 0.75rem;
      font-weight: 500;
      line-height: 1.5;
      white-space: nowrap;
      border: 1px solid transparent;
    }
    .neutral {
      background: #f4f4f5;
      color: #3f3f46;
      border-color: #e4e4e7;
    }
    .active {
      background: #ecfdf3;
      color: #05603a;
      border-color: #abefc6;
    }
    .warning {
      background: #fffaeb;
      color: #b54708;
      border-color: #fedf89;
    }
    .danger {
      background: #fef3f2;
      color: #b42318;
      border-color: #fecdca;
    }
    .info {
      background: #eff8ff;
      color: #175cd3;
      border-color: #b2ddff;
    }
  `,
})
export class Badge {
  readonly value = input.required<BadgeValue>();

  protected tone(): string {
    switch (this.value()) {
      case 'Active':
      case 'Approved':
        return 'active';
      case 'Pending':
        return 'warning';
      case 'Cancelled':
      case 'Denied':
        return 'danger';
      case 'Expired':
        return 'neutral';
      case 'Auto':
      case 'Home':
      case 'Life':
        return 'info';
      default:
        return 'neutral';
    }
  }
}

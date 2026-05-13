import type { SVGProps } from 'react';

const baseProps = {
  viewBox: '0 0 32 32',
  fill: 'none' as const,
  stroke: 'currentColor',
  strokeWidth: 1.5,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

type Props = SVGProps<SVGSVGElement>;

export function FeatureBellCheck(props: Props) {
  return (
    <svg {...baseProps} {...props}>
      <path d="M16 5 C 11 5, 8 8, 8 13 L 8 18 L 6 22 L 26 22 L 24 18 L 24 13 C 24 8, 21 5, 16 5 Z" />
      <path d="M14 25 C 14 27, 15 28, 16 28 C 17 28, 18 27, 18 25" />
      <path d="M19.5 13 L 15 17.5 L 12.5 15" />
    </svg>
  );
}

export function FeatureLockKey(props: Props) {
  return (
    <svg {...baseProps} {...props}>
      <rect x="6" y="14" width="20" height="14" rx="2" />
      <path d="M11 14 L 11 10 C 11 7, 13 5, 16 5 C 19 5, 21 7, 21 10 L 21 14" />
      <circle cx="16" cy="20" r="1.5" />
      <line x1="16" y1="22" x2="16" y2="25" />
    </svg>
  );
}

export function FeatureCloudArrow(props: Props) {
  return (
    <svg {...baseProps} {...props}>
      <path d="M9 22 C 6 22, 4 19.5, 4 17 C 4 14, 6.5 12, 9 12 C 9.5 8.5, 12.5 6, 16 6 C 20 6, 23 9, 23 13 C 25.5 13, 28 14.5, 28 18 C 28 20.5, 26 22, 23.5 22" />
      <path d="M16 17 L 16 27" />
      <path d="M12.5 23.5 L 16 27 L 19.5 23.5" />
    </svg>
  );
}

export function FeatureBarChart(props: Props) {
  return (
    <svg {...baseProps} {...props}>
      <line x1="5" y1="27" x2="27" y2="27" />
      <rect x="8" y="20" width="4" height="7" />
      <rect x="14" y="14" width="4" height="13" />
      <rect x="20" y="9" width="4" height="18" />
    </svg>
  );
}

export function ChevronDown(props: Props) {
  return (
    <svg {...baseProps} {...props} viewBox="0 0 16 16">
      <path d="M3 6 L 8 11 L 13 6" />
    </svg>
  );
}

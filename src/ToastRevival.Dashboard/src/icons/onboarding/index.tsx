import type { SVGProps } from 'react';

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

const baseProps = {
  viewBox: '0 0 32 32',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.5,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
};

export function OnboardingBell({ size = 32, ...rest }: IconProps) {
  return (
    <svg width={size} height={size} {...baseProps} {...rest}>
      <path d="M16 4 C 11 4, 8 7, 8 13 L 8 18 L 6 22 L 26 22 L 24 18 L 24 13 C 24 7, 21 4, 16 4 Z" />
      <path d="M14 25 C 14 27, 15 28, 16 28 C 17 28, 18 27, 18 25" />
      <circle cx="22" cy="9" r="2.5" />
    </svg>
  );
}

export function OnboardingTemplate({ size = 32, ...rest }: IconProps) {
  return (
    <svg width={size} height={size} {...baseProps} {...rest}>
      <rect x="6" y="5" width="20" height="22" rx="2" />
      <line x1="10" y1="11" x2="22" y2="11" />
      <line x1="10" y1="15" x2="22" y2="15" />
      <line x1="10" y1="19" x2="18" y2="19" />
      <line x1="10" y1="23" x2="14" y2="23" />
    </svg>
  );
}

export function OnboardingPackage({ size = 32, ...rest }: IconProps) {
  return (
    <svg width={size} height={size} {...baseProps} {...rest}>
      <rect x="5" y="9" width="22" height="18" rx="1" />
      <line x1="5" y1="14" x2="27" y2="14" />
      <line x1="16" y1="9" x2="16" y2="27" />
      <path d="M11 4 L 16 9 L 11 9 Z" />
      <path d="M21 4 L 16 9 L 21 9 Z" />
    </svg>
  );
}

export function OnboardingLaunch({ size = 32, ...rest }: IconProps) {
  return (
    <svg width={size} height={size} {...baseProps} {...rest}>
      <path d="M8 24 L 24 8" />
      <path d="M24 8 L 24 14" />
      <path d="M24 8 L 18 8" />
      <path d="M6 22 L 9 22" />
      <path d="M10 26 L 10 23" />
    </svg>
  );
}

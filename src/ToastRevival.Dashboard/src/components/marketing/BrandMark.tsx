import type { SVGProps } from 'react';

type Props = SVGProps<SVGSVGElement> & { size?: number };

export function BrandMark({ size = 28, ...rest }: Props) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...rest}
    >
      <path d="M16 5 C 11 5, 8 8, 8 13 L 8 18 L 6 22 L 26 22 L 24 18 L 24 13 C 24 8, 21 5, 16 5 Z" />
      <path d="M14 25 C 14 27, 15 28, 16 28 C 17 28, 18 27, 18 25" />
      <path d="M19.5 12.5 L 15.5 16.5 L 13 14" />
    </svg>
  );
}

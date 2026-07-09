import { splitProps } from 'solid-js';
import type { Component, JSX } from 'solid-js';

type LucideProps = JSX.SvgSVGAttributes<SVGSVGElement> & {
  size?: number | string;
  'stroke-width'?: number | string;
};

export interface IconProps extends JSX.SvgSVGAttributes<SVGSVGElement> {
  /** A lucide-solid icon component, e.g. `House` from 'lucide-solid'. */
  icon: Component<LucideProps>;
  size?: number | string;
  /** Accessible name. Omit for decorative icons (default: aria-hidden). */
  label?: string;
}

export function Icon(props: IconProps) {
  const [local, rest] = splitProps(props, ['icon', 'size', 'label']);
  return (
    <local.icon
      size={local.size ?? 16}
      stroke-width={1.5}
      aria-hidden={local.label ? undefined : 'true'}
      aria-label={local.label}
      role={local.label ? 'img' : undefined}
      {...rest}
    />
  );
}

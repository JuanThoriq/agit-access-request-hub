import type { ButtonHTMLAttributes } from 'react';

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'plain' | 'primary' | 'danger';
};

/** The only button in the app, so focus, disabled state and variants cannot drift apart. */
export function Button({ variant = 'plain', className, ...rest }: Props) {
  return (
    <button
      type="button"
      className={`btn${variant === 'plain' ? '' : ` ${variant}`}${className ? ` ${className}` : ''}`}
      {...rest}
    />
  );
}

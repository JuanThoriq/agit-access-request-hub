import { useEffect, type RefObject } from 'react';
import gsap from 'gsap';

/**
 * Motion here is decoration, never meaning: the UI is fully usable with animation off, and a user
 * who asked the OS for reduced motion gets no animation at all.
 */
export function prefersReducedMotion() {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * Animations set an inline opacity of 0 at their start, and a background tab does not run
 * requestAnimationFrame — so animating a page nobody is looking at would leave content invisible
 * until it is focused. When the document is hidden we simply skip the animation: the content is
 * already in its final state and nothing is lost.
 */
export function motionAllowed() {
  return !prefersReducedMotion() && typeof document !== 'undefined' && document.visibilityState === 'visible';
}

/**
 * Stagger everything matching `selector` inside `ref` when `key` changes (tab, identity, list).
 * The gsap context is reverted on cleanup, so React StrictMode's double mount does not leave two
 * animation sets fighting over the same nodes.
 */
export function useStaggerIn(ref: RefObject<HTMLElement | null>, selector: string, key: unknown) {
  useEffect(() => {
    const scope = ref.current;
    if (!scope || !motionAllowed()) return;

    const context = gsap.context(() => {
      gsap.fromTo(
        selector,
        { opacity: 0, y: 10 },
        { opacity: 1, y: 0, duration: 0.28, stagger: 0.04, ease: 'power2.out', clearProps: 'opacity,transform' },
      );
    }, scope);

    return () => context.revert();
  }, [ref, selector, key]);
}

/** Fade+lift a single element in whenever `key` changes (a different request is selected). */
export function useFadeIn(ref: RefObject<HTMLElement | null>, key: unknown) {
  useEffect(() => {
    const element = ref.current;
    if (!element || !motionAllowed()) return;

    const tween = gsap.fromTo(
      element,
      { opacity: 0, y: 8 },
      { opacity: 1, y: 0, duration: 0.22, ease: 'power2.out', clearProps: 'opacity,transform' },
    );

    return () => {
      tween.kill();
    };
  }, [ref, key]);
}

/** Modal entrance: the panel scales up while the backdrop fades. */
export function usePopIn(ref: RefObject<HTMLElement | null>) {
  useEffect(() => {
    const element = ref.current;
    if (!element || !motionAllowed()) return;

    const tween = gsap.fromTo(
      element,
      { opacity: 0, scale: 0.96 },
      { opacity: 1, scale: 1, duration: 0.2, ease: 'power2.out', clearProps: 'opacity,transform' },
    );

    return () => {
      tween.kill();
    };
  }, [ref]);
}

/** Notice banners slide in from the left; same key keeps a re-render from replaying it. */
export function useSlideIn(ref: RefObject<HTMLElement | null>, key: unknown) {
  useEffect(() => {
    const element = ref.current;
    if (!element || !motionAllowed()) return;

    const tween = gsap.fromTo(
      element,
      { opacity: 0, x: -12 },
      { opacity: 1, x: 0, duration: 0.24, ease: 'power2.out', clearProps: 'opacity,transform' },
    );

    return () => {
      tween.kill();
    };
  }, [ref, key]);
}

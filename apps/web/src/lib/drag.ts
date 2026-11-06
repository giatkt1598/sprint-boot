/**
 * The browser sends a click when a pointer drag ends, aimed at whatever is under the pointer, such as an
 * issue link. By then the dragged row may already have moved to another list, so a guard kept on the row
 * cannot catch it. Call this when a drag ends: the click that follows is swallowed before React sees it.
 */
export function swallowClickAfterDrag() {
  const swallow = (event: MouseEvent) => {
    event.preventDefault();
    event.stopPropagation();
    window.removeEventListener('click', swallow, true);
  };
  window.addEventListener('click', swallow, true);
  // The click comes right after the pointerup that ended the drag; keyboard drags never send one.
  window.setTimeout(() => window.removeEventListener('click', swallow, true), 100);
}

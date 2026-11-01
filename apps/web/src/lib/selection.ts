/** True right after a drag that selected text, which also ends in a click event. */
export function hasSelectedText() {
  const selection = window.getSelection();
  return Boolean(selection && !selection.isCollapsed && selection.toString().trim());
}

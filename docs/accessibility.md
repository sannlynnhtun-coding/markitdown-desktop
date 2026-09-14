# Accessibility verification

The UI uses native controls, visible labels, logical tab order, keyboard accelerators, theme resources, and a polite live status region. Status is communicated with text as well as visual styling.

Release acceptance requires manual checks on Windows 10 22H2 and Windows 11 x64:

- Complete the primary flow using only the keyboard.
- Verify focus returns logically after pickers and dialogs.
- Smoke-test queue status and dirty-document dialogs with NVDA.
- Verify 200% text scaling without clipped primary actions.
- Verify light, dark, and high-contrast themes.
- Verify reduced-motion preferences and that progress remains understandable.

These manual checks are not replaced by unit or build tests and must be recorded before public distribution.

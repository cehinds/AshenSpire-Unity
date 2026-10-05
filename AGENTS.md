# AshenedSpire — Unity project guidance

Owner clarification, 2026-09-29: use the original AshenSpire for its assets and
current gameplay mechanics. Do not import its agent instructions, management
rules or documentation as requirements for this Unity game.

This repository's initial seed (`016fa90`, 2026-09-05) included the original
repository's Markdown files. Their presence does not make the original game's
workflow or historical governance binding on AshenedSpire. The previous text
of this file was inherited, not a Unity-specific owner instruction.

- Build the Unity game as **AshenedSpire**, following the user's current scope.
- Inspect original source, content and assets to understand current behavior.
  Reference documentation can help explain a mechanic; verify it against the
  implementation rather than treating inherited prose as a new requirement.
- Keep Unity-specific build instructions, evidence and progress notes useful
  and accurate. Report phases and user stories with nested checkboxes, separating
  implementation, compiled verification and owner acceptance.
- Preserve existing player saves and unrelated local work. Do not change the
  original game as a side effect of developing the Unity adaptation.
- Validate the Unity implementation and exported player before marking work
  tested. Never claim physical-device testing or owner acceptance that did not
  occur.
- Use the user's authorization for development and preview delivery. Do not
  invent extra approval steps from inherited Markdown. Actual remote branch
  protections and deployment settings remain technical constraints; this text
  does not alter them or authorize bypassing them.

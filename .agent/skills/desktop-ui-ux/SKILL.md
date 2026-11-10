---
name: desktop-ui-ux
description: UI/UX design intelligence and responsive layout rules for modern dark-mode WinForms desktop applications (inspired by ui-ux-pro-max and modern desktop design systems). Use whenever creating, reviewing, or fixing forms, dialogs, tables, toolbars, themes, or resizing behavior.
---

# Modern Desktop UI/UX & Responsive WinForms Skill

## Core Design Principles
1. **Zero Hardcoded Right-Anchor Math (`page.Width - X`)**:
   - Never position controls using `parent.Width - offset` before the parent control is sited and sized.
   - Always use **`TableLayoutPanel`** (with `ColumnStyle(SizeType.Percent, 100f)` + `ColumnStyle(SizeType.AutoSize)`) and **`FlowLayoutPanel`** for toolbars, headers, and action bars so elements reflow without overlapping at any window size or DPI scale.
2. **Proportional Table Columns & Flicker-Free Rendering**:
   - Standard `ListView` controls leave empty space or clip columns on resize and render legacy light-mode headers.
   - Always use `DarkListView` (`DoubleBuffered = true`, `OwnerDraw = true` for dark column headers, and proportional column weight auto-scaling on `OnResize`).
3. **Intentional Dark Palette & Contrast (WCAG AA)**:
   - Avoid pure `#000000` backgrounds. Use layered charcoal surfaces:
     - App Background (`BgDark`): `#16181E`
     - Card / Surface (`BgCard`): `#1F222B`
     - Input / Secondary Button (`BgInput`): `#2A2E3A`
     - Subtle Border / Divider (`BorderSubtle`): `#343948`
   - Ensure primary text (`#EEF0F6`) and status colors (Success `#A6F0C3`, Warning `#FFC38C`, Ready `#B4D7FF`, Muted `#9CA3B3`) maintain high readability.
4. **Micro-Interactions & Tactile Controls**:
   - Every button must define explicit `MouseOverBackColor` and `MouseDownBackColor` states and `Cursors.Hand`.
   - Secondary buttons (`BgInput`) should include a subtle `1px` border (`#3A4050`) to distinguish them from the card surface.
   - Preserve scroll position (`TopItem`) and selection state across data refreshes so tables never jump back to the top.

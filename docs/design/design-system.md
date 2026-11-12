# PVM Backoffice design system

Status: steps A1, A2, B1 and B2 · Owner: Marius Bloemhof · 2026-11-11

This document is the authority for every visual and interaction decision in the
workbench (`frontend/workbench`). The live reference is `/ui-kit`, which shows
every component and state in development. The structure of the product (navigation,
modules, page templates, build order) is in
[`backoffice-ui-redesign.md`](backoffice-ui-redesign.md).

## How to use this document

1. Find the component for the job in [Choose a component](#choose-a-component).
2. Read its section: when to use it, its props, its states and its copy rules.
3. Import it from `src/ui`. Do not copy its markup or its CSS.
4. If no component fits, do not improvise. Add the component to `src/ui`, to
   this document and to `/ui-kit` first, in the same pull request.

Rules that apply to every screen:

- A screen uses only components from `src/ui` and tokens from `app/tokens.css`.
- No literal colour, size or space in a component or page. Use a token.
- No inline `style` in a page. Use `Stack` for layout. The only inline styles
  are inside `src/ui`, for computed values such as a bar width.
- Every page uses one of the five [page templates](#page-templates), or
  `AuthCard` when there is no signed-in user.
- To check a page locally, run `npm run mock-api` and the dev server with
  `AUTH_MODE=DevelopmentBypass` and `NEXT_PUBLIC_API_BASE_URL=http://127.0.0.1:59999`.
  The mock returns example data in the shape of `src/api/client.ts`.
- No UI framework, CSS framework or component kit. The dependency rule is in
  section 6.1 of the redesign document.

## Contents

- [Foundations](#foundations): colour, spacing, type, shape, motion, layers, focus.
- [Page templates](#page-templates): Dashboard, List, Record, Task, Settings, and the page states.
- [Choose a component](#choose-a-component).
- Components:
  - Layout: [PageFrame](#pageframe), [Columns](#columns), [Stack](#stack), [Panel](#panel), [PageHeader](#pageheader), [TileGrid](#tilegrid), [AppShell](#appshell).
  - Actions: [Button](#button), [ButtonLink](#buttonlink), [SubmitButton](#submitbutton), [ConfirmDialog](#confirmdialog).
  - Content: [CodeBlock](#codeblock).
  - Status: [StatusBadge](#statusbadge), [Notice](#notice), [Spinner](#spinner), [EmptyState](#emptystate).
  - Forms: [Fields](#fields), [FileDrop](#filedrop).
  - Lists: [DataTable](#datatable), [FilterChips](#filterchips), [FilterBar](#filterbar), [Pagination](#pagination).
  - Navigation: [TabNav and Tabs](#tabs), [BackLink](#backlink), [Stepper](#stepper).
  - Records: [KeyValueList](#keyvaluelist), [Timeline](#timeline).
  - Dashboards: [AttentionList](#attentionlist), [StatTile](#stattile), [Tile](#tile), [BarList](#barlist).
  - Automation: [ModeSwitch](#modeswitch).
- [Copy rules](#copy-rules).
- [Patterns that are not components](#patterns-that-are-not-components).

---

## Foundations

All values live in `app/tokens.css`. The light theme is on `:root`. The dark
theme is on `:root[data-theme="dark"]`.

The dark theme follows the operating-system setting through
`prefers-color-scheme`. `data-theme="light"` or `data-theme="dark"` on `<html>`
overrides it. The `/ui-kit` theme switch sets that attribute.

### Colour

Neutrals have a slight green tint from the brand hue. The brand colour is for
interactive elements only: buttons, links, the selected item. A heading or an
icon that is not interactive never uses the brand colour.

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--color-canvas` | `#f5f6f5` | `#101612` | Page background |
| `--color-surface` | `#ffffff` | `#172019` | Panels, tables, fields, dialogs |
| `--color-fill` | `#eef1ef` | `#1f2923` | Table headers, quiet areas, disabled fields |
| `--color-text-strong` | `#1f2421` | `#eef2ef` | Body text, values |
| `--color-text-weak` | `#5b6660` | `#a3aea7` | Labels, hints, secondary text |
| `--color-stroke-strong` | `#848f89` | `#75817a` | Field and chip borders |
| `--color-stroke-weak` | `#e1e6e2` | `#2b352f` | Dividers, panel borders |
| `--color-brand` | `#14532d` | `#6fbf8f` | Buttons, links, selected state |
| `--color-brand-hover` | `#0f3f22` | `#8ccfa6` | Hover of brand elements |
| `--color-brand-fill` | `#e7f1ea` | `#1d3326` | Hover rows, selected chips |
| `--color-on-brand` | `#ffffff` | `#0d1a12` | Text on a brand fill |
| `--color-error` / `-fill` | `#b42318` / `#fef3f2` | `#f97066` / `#3a1714` | Failed, rejected, errors |
| `--color-warning` / `-fill` | `#93370d` / `#fffaeb` | `#fdb022` / `#33260b` | Needs review, overdue, low |
| `--color-success` / `-fill` | `#067647` / `#ecfdf3` | `#47cd89` / `#0f2e1d` | Succeeded, ready, healthy |
| `--color-info` / `-fill` | `#175cd3` / `#eff8ff` | `#84adff` / `#102a4c` | Shadow, running, information |
| `--color-neutral` / `-fill` | `#475467` / `#f2f4f7` | `#b0b8c4` / `#23282f` | Off, disabled, queued |
| `--color-sidebar` | `#16211b` | `#0b110d` | Sidebar background |
| `--color-sidebar-text` / `-weak` | `#e6ebe8` / `#9aa8a0` | `#e6ebe8` / `#94a39a` | Sidebar text |
| `--color-sidebar-current` | `#24392c` | `#1f3326` | Current sidebar item |
| `--color-band-production` | `#b42318` | `#7a1a12` | Production band |
| `--color-backdrop` | 48 % dark | 64 % black | Behind a modal dialog |

Contrast, checked with the WCAG formula (minimum 4.5:1 for text, 3:1 for borders):

| Pair | Light | Dark |
| --- | --- | --- |
| Text weak on surface | 6.0 | 7.3 |
| Text weak on fill | 5.3 | 6.6 |
| Stroke strong on surface (border) | 3.4 | 4.1 |
| Brand on brand fill | 7.9 | 6.1 |
| On-brand on brand | 9.1 | 8.1 |
| Each status colour on its fill | 5.4 to 7.2 | 5.7 to 8.0 |
| Danger button text | 6.6 | 6.0 |
| Production band text | 6.6 | 8.8 |
| Sidebar weak on sidebar current | 5.0 | 5.1 |

Status colour always comes with an icon and a word. Colour is never the only
signal.

### Spacing

An 8-point scale. Less space between related items, more space between groups.

| Token | Value | Use |
| --- | --- | --- |
| `--space-2xs` | 4px | Label to its control, icon to its word |
| `--space-xs` | 8px | Inside a control group, between chips |
| `--space-s` | 16px | Between controls, between buttons, panel header padding |
| `--space-m` | 24px | Panel padding, between panels in a group |
| `--space-l` | 32px | Between form fields, below the page header |
| `--space-xl` | 48px | Between page sections |
| `--space-xxl` | 80px | Bottom padding of a page |

If you are not sure between two steps, use the larger one.

### Type

Inter, with system fallbacks. Two weights only: `--font-regular` (400) and
`--font-bold` (600). Identifiers use `--font-mono`.

| Token | Size / line height | Use |
| --- | --- | --- |
| `--text-h1` | 28 / 36 | Page title. One per page |
| `--text-h2` | 20 / 28 | Page section title, dialog title |
| `--text-h3` | 16 / 24 | Panel title |
| `--text-body` | 16 / 24 | Body text, field values, notice titles in dialogs |
| `--text-small` | 14 / 20 | Tables, hints, labels, buttons, secondary text |
| `--text-caption` | 12 / 16 | Table headers, badges, eyebrows |

Use mono type for these values only: invoice numbers, statement numbers, cash
accounts, GLNs, GTINs, item codes, PO numbers. Use tabular figures
(`font-variant-numeric: tabular-nums`) for every column of numbers.

### Shape, depth, motion and layers

| Token | Value | Use |
| --- | --- | --- |
| `--radius-s` | 6px | Swatches, small marks, file button |
| `--radius-m` | 8px | Buttons, fields, sidebar items |
| `--radius-l` | 12px | Panels, tiles, notices, dialogs |
| `--shadow-raised` | small, sharp | Panels (none in dark) |
| `--shadow-overlay` | large, soft | Dialogs |
| `--duration-fast` | 120ms | Hover and colour changes |
| `--duration-slow` | 240ms | Panels that open |
| `--layer-sticky` | 10 | Sticky headers |
| `--layer-overlay` | 100 | Overlays that are not native dialogs |
| `--control-height` | 40px | Buttons, fields, segmented controls |
| `--field-width-s` / `-m` / `-l` | 160 / 240 / 400px | Field widths |

The global `prefers-reduced-motion` rule removes animation for users who ask
for it.

### Focus

Every interactive element shows `--focus-ring` (2px brand outline, 2px offset)
on keyboard focus. Never remove it. Inside the dark sidebar, the ring uses the
sidebar text colour.

### Utilities

`.sr-only` hides text from the screen and keeps it for screen readers. Use it
for table captions and for the names of icon-only buttons.

---

## Page templates

Every page is one of five templates. A template fixes the order of the parts
and the space between them. The page supplies the content. Examples with
example data are at `/ui-kit/templates` in development.

| Template | Use for | Component | Width |
| --- | --- | --- | --- |
| Dashboard | Today and each module overview | `DashboardPage` | Wide, 1280px |
| List | Records with filters, sorting and pages | `ListPage` | Wide |
| Record | One invoice, statement, run or automation | `RecordPage` | Wide |
| Task | A guided job that writes to another system | `TaskPage` | Narrow, 864px |
| Settings | Forms, saved one section at a time | `SettingsPage` and `SettingsSection` | Narrow |

To choose a template:

- If the page shows many records, use List.
- If the page shows one record, use Record.
- If the user changes data in Acumatica, Shoprite, a bank or the web store, use Task.
- If the user changes how the backoffice behaves, use Settings.
- If the page summarises a module, use Dashboard.

### DashboardPage

Order, top to bottom. The order does not change:

1. `header`. On Today, the eyebrow is the date. The description counts what
   needs the user: "3 items need you. Everything else is running."
2. `notice` (optional): only for a state that blocks work, such as an emergency
   stop.
3. Needs attention: `attention.items` in an `AttentionList`. When there are no
   items, one quiet success line shows `attention.allClear`: "Nothing needs
   you. Last checked 10:40."
4. Automations (optional): `Tile`s in a `TileGrid`, with the link "All
   automations".
5. Key numbers (optional): 2 to 4 `StatTile`s in a `TileGrid`.
6. Recent activity (optional): a `DataTable`, with the link "All runs". The
   template puts it in a flush `Panel`. `activity.title` renames the section,
   such as "Recent statements". At most 5 rows on Today.

Do not add other sections. Put more detail on the List pages of the module.

### ListPage

Order: `header`, `sections` (a `TabNav` when the list is one section of a
module), `notice`, `filters`, then `table` and `pagination` together in one
flush panel.

- The header action is the primary action for the list, such as "Import
  statement". It is never a filter.
- `filters`: `FilterChips` for one value with up to 8 options, otherwise
  `FilterBar`.
- Each row opens its Record page through `rowHref`.
- `attention` (optional): a short second list that needs a person before the
  main list is useful, in a flush `Panel`, above the filters. Example: the
  Shoprite items without a mapping, above the mappings.
- `table` can be `null` when the user has no access. Then the `notice` says why.
- Bulk actions: none yet. When a page needs them, add row selection to
  `DataTable` and a selection bar to this document first.

### RecordPage

Order: `back` link, `header`, `notice`, then `summary` and `activity` side by
side (one column on a phone), then `sections` (a `TabNav`), then `children`.

- `header.title` is the identifier: "INV190970". `header.description` is the
  one-line context: store, date, total.
- `header.actions`: the `StatusBadge` first, then at most one primary action.
  The primary action leads to the next step for the record in its current
  status. For an invoice that needs review, that is "Open in Acumatica", not
  "Submit invoice".
- `notice`: why the record needs a person, and what to do.
- `summary`: a `KeyValueList` of 4 to 8 facts.
- `activity`: a `Timeline`, newest first, at most 10 entries.
- `sections`: Details and Raw. Raw (payloads) is for Admin only.
- `back` is optional only for a record that has no list yet, such as the
  Shoprite automation policy until step C2.

### TaskPage

Order: `back` link, `header`, `Stepper`, then the content of the current step.

Steps:

1. Choose input: a form in one `Panel`, with a `SubmitButton` that checks the
   input: "Check statement".
2. Check what will change: a summary `Panel` (`KeyValueList`) and a `Panel`
   with a `DataTable` that shows each change and what will happen to it. The
   action names the count: "Import 3 lines". A tertiary link starts again.
3. Confirm (optional): a `ConfirmDialog` when the change reaches Shoprite, a
   bank or production data that cannot be undone.
4. Done: a success `Notice` with `live`, then two links: back to the module
   (primary) and "do another" (secondary).

Rules:

- Every page that writes to Acumatica, Shoprite, a bank or the web store is a
  Task page with a "check what will change" step. No write without a preview.
- Keep the current step in the URL (`?step=`) or in the server response, so a
  reload does not lose the place of the user.
- If a step fails, stay on the step and show an error `Notice` above its panel.

### SettingsPage and SettingsSection

A `SettingsPage` holds one `SettingsSection` per topic. Each section has its
own form, its own server action and its own save button.

- `submitLabel` names the topic: "Save schedule". Never "Save" alone.
- The save button is secondary, because a page has several of them.
- `result`: after a save, a success `Notice` with the time ("Schedule saved at
  10:52"), or an error `Notice` with the number of errors.
- Fields follow [Fields](#fields).

### Signed-out pages

`AuthCard` is the only layout for a page without a signed-in user: sign-in and
"No access yet". It has the product name, a title, one sentence, an optional
`Notice` and one action. The sidebar does not show on these pages.

### Page states

Every route has three states in addition to its content:

| State | File | Component |
| --- | --- | --- |
| Loading | `loading.tsx` | `PageLoading` with the page title and a label: "Loading invoices…" |
| Error | `error.tsx` | `PageError` with the page title, what could not load, and "Try again" |
| Empty | in the page | `EmptyState` inside the panel where the content goes |

`PageError` says what failed and what to do. It does not show the technical
error to the user. The server logs it.

---

## Choose a component

| You need to | Use |
| --- | --- |
| Build a page | One of the [page templates](#page-templates) |
| Lay out siblings with a gap | `Stack` |
| Put two or three panels side by side | `Columns` |
| Group content with a title on a surface | `Panel` |
| Title a page, with its actions | `PageHeader` |
| Start the main action of the screen | `Button variant="primary"` (one per screen) |
| Go to another page from a button | `ButtonLink` |
| Submit a form and show progress | `SubmitButton` |
| Confirm an action that is hard to undo | `ConfirmDialog` |
| Show the state of a record or run | `StatusBadge` |
| Tell the user about the page, or the result of an action | `Notice` |
| Show a list of records | `DataTable` in a flush `Panel` |
| Filter a list by one value | `FilterChips` |
| Filter a list by several values | `FilterBar` |
| Page through a long list | `Pagination` |
| Sections of a module or record, each with a URL | `TabNav` |
| Two views inside one page, no URL | `Tabs` |
| Go back to the list | `BackLink` |
| Show progress through a task | `Stepper` |
| Show facts about one record | `KeyValueList` |
| Show what happened to a record | `Timeline` |
| List what needs a person | `AttentionList` |
| Show a key number | `StatTile` in a `TileGrid` |
| Link to an object with its state | `Tile` in a `TileGrid` |
| Compare one measure across items | `BarList` |
| Set an automation to Off, Shadow or Live | `ModeSwitch` |
| Wait for something | `Spinner` |
| Show that a list or area is empty | `EmptyState` |
| Collect input | The field components in [Fields](#fields) |

---

## Layout

### PageFrame

The `<main>` element of a converted page. It sets the width (`wide`, 1280px,
or `narrow`, 864px), the page padding and `data-ds`. The space between its
children is `--space-l`. The templates use it. A page uses it directly only
when the page is not one of the five templates, such as `/ui-kit`.

Inside a `PageFrame`, `PageHeader` has no bottom margin. The template sets the
space.

### Columns

Two or three panels side by side, each at least 340px wide. On a narrow
screen, they stack. `RecordPage` uses it for the summary and the activity.

### Stack

Lays out its children in a column or a row with a gap from the spacing scale.

| Prop | Type | Default | Notes |
| --- | --- | --- | --- |
| `gap` | `2xs` to `xxl` | `m` | Refer to [Spacing](#spacing) |
| `direction` | `column`, `row` | `column` | Rows centre their items |
| `wrap` | boolean | false | Rows only |
| `align` | `start`, `center`, `end`, `stretch`, `baseline` | — | Cross axis |
| `justify` | `start`, `between`, `end` | — | Main axis |
| `as` | `div`, `section`, `ul`, `ol` | `div` | Removes list bullets and margins |

Use `Stack` instead of margins. Page sections: `gap="xl"`. Panels in a section:
`gap="m"`. Buttons in a row: `direction="row" gap="s"`.

### Panel

A surface with an optional title, description and actions.

| Prop | Notes |
| --- | --- |
| `title` | Sentence case, a noun: "Recent statements" |
| `description` | One line of context |
| `actions` | Secondary buttons or a link. Never the primary action of the page |
| `flush` | Removes the body padding, for a `DataTable` or an `AttentionList` |

Do not put a panel inside a panel. Use a `Stack` gap instead.

### PageHeader

The top of every page: eyebrow, title, description, actions.

- `eyebrow`: the module or area, such as "Banking". Leave it out on Today.
- `title`: the page name. It is the only `h1`.
- `description`: what the page is for, in one sentence.
- `actions`: at most one primary button. Put it last.

### TileGrid

A responsive grid for `StatTile` and `Tile`. `min` is the smallest tile width
in pixels (default 200). The grid becomes one column on a phone.

### AppShell

The sidebar, the environment band and the content area. It is set once in
`app/layout.tsx`. Pages do not use it directly. The navigation items are in
`src/navigation.ts`, filtered by role.

The environment band is red in production and amber in QA. There is no band in
development.

---

## Actions

### Button

| Variant | Look | Use |
| --- | --- | --- |
| `primary` | Brand fill, on-brand text | The most important action. One per screen |
| `secondary` | Brand border and text | All other actions. The default |
| `tertiary` | Underlined brand text | Low-priority actions, Cancel, destructive triggers |
| `danger` | Error fill | The confirm button in a destructive `ConfirmDialog` only |

States: default, hover (darker or filled), focus (ring), active (moves down 1px),
disabled (45 % opacity). Height is `--control-height`. An icon goes before the
label, 16px, with `aria-hidden="true"`.

Rules:

- The label is a verb and a noun: "Import statement", "Run now", "Export CSV".
  Never "OK", "Submit", "Go" or "Click here".
- Do not disable a button to show that a form is incomplete. Keep it enabled
  and show the errors when the user submits.
- Do not make the first button of a destructive action red. Use `tertiary`,
  then confirm in a `ConfirmDialog` with `tone="danger"`.
- On a phone, buttons in `FormActions` stack at full width.

### ButtonLink

A link with a button look. Use it when the action opens another page, such as
"Open automation". Same variants as `Button`. Never use `ButtonLink` to start
a change. A change uses a form and `SubmitButton`.

### SubmitButton

The submit button of a form that calls a server action. While the action runs,
it shows a spinner and `pendingLabel`, and a second press does nothing.

- `pendingLabel` uses the verb with "…": "Importing…", "Saving…", "Switching…".
- Default variant is `primary`.

### ConfirmDialog

A modal confirmation, built on the native `<dialog>` element. Focus moves into
the dialog, Esc and a press on the backdrop close it, and focus returns to the
trigger. The dialog stays open with a spinner until the action finishes, then
closes.

| Prop | Notes |
| --- | --- |
| `triggerLabel`, `triggerVariant` | The button that opens the dialog. `tertiary` for destructive actions |
| `title` | A question that names the action and the object: "Submit INV190968 to Shoprite?" |
| `children` | The consequence in plain words. Say if it cannot be undone |
| `confirmLabel` | Repeats the action: "Submit invoice". Never "Yes" or "OK" |
| `pendingLabel` | "Submitting…" |
| `tone` | `danger` only when data is lost |
| `action`, `fields` | The server action and its hidden fields |

Use a dialog for: production actions that reach an outside system (Shoprite
submission, a bank import), and deletes. Do not use a dialog for a mode change
(use `ModeSwitch`) or for information (use `Notice`).

### CodeBlock

A read-only payload: generated XML, a raw JSON response. It scrolls inside
itself, up to 480px high. `language="json"` formats JSON with indents. `empty`
says why there is no payload. Payloads are for Admins: put them in a Raw tab.

---

## Status

### StatusBadge

A status as an icon and a word. It maps any status value to a tone: for
example `DeadLettered` becomes "Dead lettered" in error colour.

| Tone | Icon | Values |
| --- | --- | --- |
| success | circle with check | Succeeded, Ready, Healthy, Submitted, Accepted, Live, Processed, Resolved |
| error | circle with cross | Failed, Rejected, Blocked, Failing, Dead lettered |
| warning | circle with mark | Needs review, Stale, Late, Open, Ambiguous |
| info | circle with i | Shadow, Running, In progress |
| neutral | dashed circle | Off, Disabled, Queued, and every value that is not known |

Pass `tone` or `label` only when the value itself is wrong for the reader. To
add a status value, add it to `toneByStatus` in `src/ui/status-badge.tsx` and to
this table.

### Notice

A message about the page, or the result of something the user did. There are
no toasts. A result shows on the page where the user acted, and stays there.

| Prop | Notes |
| --- | --- |
| `tone` | `info`, `success`, `warning`, `error` |
| `title` | What happened, in one short sentence: "Statement 001319 created" |
| `children` | What it means or what to do next |
| `action` | One `ButtonLink` or tertiary `Button` |
| `live` | Set for a result after an action, so screen readers announce it |

An error notice says what happened and what to do: "Acumatica sign-in timed out
after 100 s. Nothing was written. The next run reads the same days again." Put
a notice above the content it is about.

### Spinner

Shows that something is in progress. With `label`, the label shows next to it.
Without `label`, screen readers hear "Loading". Use `size="s"` inside a button.
Show a spinner only for waits longer than about 300ms.

### EmptyState

The content of a list or area that has nothing in it.

- `title` names the empty space: "No statements imported yet". No apology.
- `description` says how to fill it.
- `action` is the action that fills it, if the user can do it.

A filtered list with no results says so and offers to clear the filter:
"No invoices match this filter", action "Show all invoices".

---

## Forms

### Fields

Components: `TextField`, `TextareaField`, `SelectField`, `FileField`,
`CheckboxField`, `RadioGroup`, `FormLayout`, `FormActions`.

Anatomy, top to bottom: label, hint, error, control. The label is above the
control, never in it. The hint and the error are between the label and the
control, so the keyboard and autofill do not cover them.

| Prop | Notes |
| --- | --- |
| `name`, `id` | `id` defaults to `name` |
| `label` | Sentence case, a noun: "Cash account". No colon |
| `hint` | The format or the source: "10 digits, from the Shoprite portal" |
| `error` | What is wrong and how to fix it: "Enter a date after 13 Sep 2026" |
| `required` | Required fields show " *". Fields without `required` show "(optional)" |
| `width` | `s` (160px), `m` (240px), `l` (400px), `full`. Match the expected input |
| `showOptional` | `false` hides "(optional)". Use it only in a `FilterBar` |

States: default, hover (darker border), focus (ring and brand border), invalid
(2px error border, `aria-invalid`, error text linked by `aria-describedby`),
disabled (fill background).

Choose the control:

| Input | Control |
| --- | --- |
| Free text, numbers, dates | `TextField` with the right `type` and `inputMode` |
| Long text | `TextareaField` |
| One choice from 2 to 10 options | `RadioGroup` |
| One choice from more than 10 options | `SelectField` |
| One on/off value that applies on submit | `CheckboxField`, with a positive label |
| A file | `FileField`, with `accept` and a hint that names where to get the file |

Layout: put the fields in `FormLayout` (one column, `--space-l` between fields,
640px maximum). Put `FormActions` last: `SubmitButton` first, then a tertiary
Cancel. Validate on the server. When the server returns errors, show an error
`Notice` above the form with the number of errors, and the error on each field.

### FileDrop

The first step of a Task page that starts from a file. Drop a file on it, or
press "Choose a file". `onFile` receives the file, and the page checks it at
once, without a second button. `busy` shows a spinner with `busyLabel` while
the page reads the file. The hint names the file type and where to get it.

`CheckboxGroup` puts several `CheckboxField`s under one label, for a choice of
more than one option, such as user roles.

---

## Lists

### DataTable

A table of records. The server returns the rows. Sorting, filtering and paging
live in the URL, so a list can be shared and reloaded.

| Prop | Notes |
| --- | --- |
| `caption` | For screen readers: "Shoprite invoices" |
| `columns` | `key`, `header`, `render`, and optional `align: "end"`, `mono`, `weak`, `sortable` |
| `rows`, `rowKey` | The records and a stable key |
| `rowHref` | The record page. The first cell becomes the link and the whole row is clickable |
| `sort`, `sortHref` | The current sort and a function that builds the URL for a sort |
| `empty` | An `EmptyState` for no rows |

Rules:

- Put the table in `<Panel flush>`. Put `Pagination` after the table, in the
  same panel.
- Headers are sentence case: "Total incl. VAT".
- Numbers and money align to the end. Identifiers use `mono`.
- The first column identifies the record. If the row has a page, the first
  column is the link.
- At most 7 columns. Put more detail on the record page.
- An action column is the last column. It holds one tertiary `ButtonLink` per
  row, such as "Map item". Use it only when the row has no record page.
- A status column uses `StatusBadge`.
- On a phone, the table scrolls sideways inside its panel.

### FilterChips

Quick filters above a list, one choice at a time. Each chip is a link that sets
a URL parameter. Show `count` when the API returns it. The first chip is "All".
For more than 8 values or for combined filters, use `FilterBar`.

### FilterBar

Filters for a list with more than one value to filter by. It is a GET form, so
the filters go in the URL and it works without JavaScript.

| Prop | Notes |
| --- | --- |
| `action` | The path of the list |
| `label` | For screen readers: "Filter invoices" |
| `keep` | URL parameters to keep, such as the tab or the sort |
| `activeCount` | The number of filters in use. With 1 or more, "Clear filters (n)" shows |
| `children` | Fields, each with `showOptional={false}` |

Fields in a filter bar:

- A search `TextField` with `type="search"`. The label names what it searches: "Invoice or store".
- A `SelectField` with a placeholder such as "All stores".
- Date `TextField`s.

Use at most 4 fields. The button is "Apply filters".

### Pagination

"1–50 of 312" and the links "Previous page" and "Next page". Leave `total` out
when the API does not return it. Show only the links that apply.

---

## Navigation

### Tabs

`TabNav`: each tab is its own URL. Use it for the sections of a module
(Invoices, Purchase orders, Reference data) and for the parts of a record. The
current tab has `aria-current="page"`. `count` shows a number next to a label.

`Tabs`: panels inside one page that need no URL, such as two views of the same
data. Keyboard: Left and Right move between tabs, Home and End jump to the
ends. Content of hidden panels stays in the page.

Labels are short nouns. At most 6 tabs.

### BackLink

The link from a Record or Task page up to its list. The label names the list:
"Shoprite invoices". Never "Back". It goes above the `PageHeader`.

### Stepper

Progress through a Task page of 3 to 5 steps. `current` counts from 0. Done
steps show a check mark, the current step is bold with a brand dot, and later
steps are weak. Step names are short: "Choose file", "Check what will change",
"Done".

---

## Records

### KeyValueList

Labelled facts about one record. A `null` or `undefined` value shows "Not set"
in weak text. `mono` for identifiers. `layout="stacked"` puts each label above
its value, for narrow panels. On a phone, all lists stack.

### Timeline

What happened to one record or automation, newest first. Each entry has a
formatted `time` ("11 Nov, 05:00"), an ISO `dateTime`, a `title` in the past
tense ("Invoice finalised in Acumatica"), an optional `detail`, and an
optional `status` (a `StatusBadge`).

---

## Dashboards

### AttentionList

Items that need a person, most severe first. The list sorts itself by tone:
error, warning, info, neutral, success. Each item has a `status` word, a
`title` that names the object and the problem, a `detail` with the consequence
or the fix, and one `action`. Put it in `<Panel flush>`. On a phone, each item
stacks.

### StatTile

One key number. `label` names what is counted, `value` is formatted, `detail`
gives context ("0 sent, submission in shadow"). `href` opens the list behind the
number. `tone="warning"` or `"error"` adds a coloured left edge, only when the
number itself needs attention. Put 2 to 4 tiles in a `TileGrid`.

### Tile

A link to one object with its state: an automation, a report. `title`,
`status` (a `StatusBadge`) and `meta` ("Live · daily 05:00").

### BarList

Horizontal bars that compare one measure across up to 12 items. Every bar
prints its value, so the chart needs no tooltip. `low` marks a bar below its
target with warning colour and the word "Low". `axis` takes up to three tick
labels. The chart has a screen-reader caption from `label`.

For trends over time, step C7 adds a `Sparkline` and a column chart. Until then,
do not add a chart library.

---

## Automation

### ModeSwitch

Off, Shadow or Live for one automation. A press on a new mode opens an inline
confirmation that names the change and says what the new mode does. The
confirmation is amber when the new mode is Live. It stays open with a spinner
until the action finishes.

| Prop | Notes |
| --- | --- |
| `automation` | The name: "Investec feed" |
| `current` | The mode now |
| `consequences` | One sentence per mode, for this automation |
| `action` | A server action. It receives the field `mode` |

The sentence for the current mode shows under the switch at all times.

---

## Copy rules

- Sentence case everywhere: titles, labels, buttons, table headers.
- Buttons: a verb and a noun. Links: name the destination.
- Numbers as numerals. Money as "R 18 422.40" (space as the thousands
  separator, two decimals).
- Dates as "11 Nov 2026" and times as "05:00", in South African time. In
  tables and timelines, leave out the year for dates in the current year.
- Errors say what happened and what to do. Never blame the user.
- Use the names the users know: "statement", "cash account", "Shoprite PO",
  "store or DC". Show internal codes (PVMINVEST, CA306500) next to the name,
  not instead of it.
- No exclamation marks. No "please" in labels. No "my".

## Patterns that are not components

| Pattern | Do |
| --- | --- |
| A result after an action | A `Notice` with `live` at the top of the area the user acted on |
| Toasts | Not used. Refer to `Notice` |
| A destructive action | Tertiary trigger, `ConfirmDialog` with `tone="danger"` |
| Loading a page | `loading.tsx` with `PageLoading`. Refer to [Page states](#page-states) |
| A failed page load | `error.tsx` with `PageError` |
| Production safety | The red environment band, plus a `ConfirmDialog` for actions that reach Shoprite, a bank or Acumatica |
| Icons | `lucide-react`, 16px in buttons, 18px in navigation and notices, always with a word |

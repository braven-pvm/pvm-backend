# PVM Backoffice — UI redesign

Status: approved direction, slice 0 in progress · Owner: Marius Bloemhof · 2026-11-11

The workbench started as an admin console for one integration (Shoprite invoice
submission). It is becoming the operating surface for PVM's back office:
banking, stock, Shoprite, reports and a growing set of automations. This
document sets the structure, the design system and the build order, so every
new screen lands in the right place and looks like it belongs.

## 1. Where it is today

| Area | Finding | Consequence |
| --- | --- | --- |
| Navigation | 11 flat links in one top bar, in no order. They wrap to two lines at normal widths and show to signed-out visitors. | No sense of place. Every new feature makes it worse. |
| Grouping | Daily work (Invoices, Bank import), configuration (Users, Inventory mappings) and plumbing (Messages, Webhook events) sit side by side. | Operators see internals they never need; admins hunt for settings. |
| Identity | Titled "PVM Integration Workbench". | Describes the first project, not the product. |
| Home | "Control Room" reports Shoprite-only health (PO freshness, invoice sync). | Banking, stock and new automations have no place on it. Nothing says what needs a person. |
| Automations | Only the Shoprite policy has a screen. The Investec feed, reconciliation and stock sync have none. | No one can see whether a scheduled process ran, failed or is switched off. |
| Environment | "PROD" is a small grey badge. | Production and QA look identical. |
| Styling | One 1,744-line `globals.css`, 127 page-specific classes, 8 colour variables, no type or spacing scale. | Each page restyles the same things; consistency depends on copy-paste. |
| Components | None shared. `Status` is redefined per page. | Same concept, different look and behaviour per page. |
| API client | One 1,062-line `client.ts`. | Hard to find things; every module grows the same file. |

What works and stays: Next.js App Router with server components and server
actions, Entra sign-in, role checks (`Admin`, `Operator`, `Viewer`), the green
brand colour, and the honest, plain copy.

## 2. Principles for this product

1. **Attention first.** The first thing on any overview is what needs a
   person. Healthy things are quiet.
2. **Every automation is visible and controllable.** Anything that runs on its
   own has a page: what it does, its mode, its last run, its next run, and a
   way to stop it.
3. **One place per job.** A module owns its work, its history and its settings.
   Cross-cutting views (runs, events, exceptions) aggregate across modules but
   always link back to the owning module.
4. **Production is unmistakable.** A red band on every production page.
5. **Shadow before live.** Any automation that writes to an outside system has
   three modes — Off, Shadow, Live — and the UI makes the mode obvious.
6. **Engineered, not decorated.** Every value comes from the token set; every
   choice has a reason.

## 3. Information architecture

### 3.1 Navigation

A left sidebar, grouped by intent. Groups are ordered by how often people use
them.

| Group | Item | Route | Who | Purpose |
| --- | --- | --- | --- | --- |
| — | Today | `/` | All | What needs attention across everything |
| — | Exceptions | `/exceptions` | Operator+ | The work queue; shows an open count |
| Operations | Shoprite | `/shoprite` | All | Invoices, purchase orders, reference data |
| Operations | Banking | `/banking` | Operator+ | Statement import, feeds, statement history |
| Operations | Web store stock | `/stock` | Operator+ | Stock sync to WooCommerce, product mapping |
| Automation | Automations | `/automations` | All | Every scheduled or automatic process |
| Automation | Runs | `/runs` | All | Execution history across all automations |
| Automation | Events | `/events` | Admin | Inbound webhooks and queue messages |
| Insight | Reports | `/reports` | All | Read-only analysis and exports |
| Settings | Connections | `/settings/connections` | Admin | Health of Acumatica, Shoprite, banks, WooCommerce |
| Settings | Users | `/settings/users` | Admin | People and roles |

Rules:

- Items a user's role cannot use are hidden, not disabled.
- A new business area (for example a second retailer) is a new item under
  Operations, never a new top-level group.
- The sidebar collapses to icons on narrow screens and becomes a menu on
  mobile.

### 3.2 Module structure

Every module under Operations has the same shape, so learning one teaches all:

| Route | Page | Template |
| --- | --- | --- |
| `/<module>` | Overview: attention, automation status, key numbers | Dashboard |
| `/<module>/<things>` | A list of the module's records | List |
| `/<module>/<things>/[id]` | One record | Record |
| `/<module>/<task>` | A guided job, such as importing a statement | Task |
| `/<module>/settings` | Module configuration and mappings | Settings |

Mapped to today's modules:

| Module | Overview shows | Lists | Tasks | Settings |
| --- | --- | --- | --- | --- |
| Shoprite | Invoices needing review, submission mode, ready vs sent | Invoices, Purchase orders | Submit invoice | Reference data (locations→GLN, items→GTIN), inventory mappings |
| Banking | Last statement per account, overdue accounts, Investec feed health | Statements | Import statement | Account mapping (bank account → cash account) |
| Web store stock | Pending changes in shadow, last sync, products out of sync | Sync runs, product differences | Review and apply changes | Product mapping (InventoryCD → WooCommerce id), publishing rules |

### 3.3 Cross-cutting views

- **Automations** (`/automations`, `/automations/[key]`): one row per
  automation with module, trigger, mode, last result, next run. The detail page
  carries the controls: mode switch, Run now, emergency stop, recent runs,
  configuration summary. The Shoprite automation policy moves here.
- **Runs** (`/runs`, `/runs/[id]`): every execution, filterable by automation,
  status and date. A run page links to its automation and its module records.
- **Events** (`/events`): Acumatica webhook deliveries and queue messages in
  one stream with a source filter. Today's "Messages" and "Webhook events"
  pages merge here.
- **Exceptions** (`/exceptions`): stays top-level because it is the work
  queue.

### 3.4 Redirects

Old URLs keep working through `next.config.ts` redirects:

| Old | New |
| --- | --- |
| `/invoices`, `/invoices/:id` | `/shoprite/invoices`, `/shoprite/invoices/:id` |
| `/purchase-orders`, `/purchase-orders/:id` | `/shoprite/purchase-orders`, `/shoprite/purchase-orders/:id` |
| `/automation` | `/automations/shoprite-submission` |
| `/admin/users` | `/settings/users` |
| `/admin/inventory-mappings` | `/shoprite/settings/inventory-mappings` |
| `/admin/messages`, `/admin/acumatica-events` | `/events` |

`/banking` stays where it is; the current import page moves to
`/banking/import`.

## 4. The automation model

This is the concept the product is missing. Every process that runs without a
person is registered as an automation:

| Field | Example |
| --- | --- |
| Key | `investec-feed` |
| Name | Investec feed |
| Module | Banking |
| Trigger | Schedule `0 3 * * *` (05:00 SAST) · Event · Manual |
| Mode | Off · Shadow · Live |
| Last run | 11 Nov 05:00, succeeded, 10 lines imported |
| Next run | 12 Nov 05:00 |
| Health | Healthy · Late · Failing · Off |
| Controls | Run now, change mode, stop |

Known automations today: Investec feed, Acumatica invoice reconciliation,
invoice lookback, Shoprite PO refresh (off), Shoprite submission (shadow),
stock sync (planned).

Health is derived, not stored: **Late** when the expected run time has passed
by more than one interval without a run; **Failing** when the last run failed.

Slice 2 starts with a frontend registry over the existing endpoints. The
durable version needs a small backend registry (`GET /api/automations`) so
schedules and modes live in one place; that is a backend task for the same
slice.

## 5. Page templates

Five templates. Every screen is one of these.

**Dashboard** — page header, then in this order: needs attention (a list, most
severe first), automation status (tiles), key numbers, recent activity.
Healthy states collapse to one quiet line.

**List** — page header with the primary action, a filter bar, a table with
sortable columns, pagination, and empty, loading and error states. Row click
opens the record. Bulk actions appear only after a selection.

**Record** — header with identifier, status and the single primary action;
a summary panel of key facts; then tabs: Details · Activity (timeline and
audit) · Raw (payloads, for Admin).

**Task** — a guided job in steps: choose input → preview what will change →
confirm → result. Anything that writes to Acumatica, Shoprite or the store
shows a preview first. The bank import is the reference.

**Settings** — single-column forms, labels above fields, save per section.

## 6. Design system

### 6.1 Approach

Plain CSS with design tokens as CSS custom properties, components as React
server components in `src/ui/`, each with a CSS Module. No Tailwind: adopting
it means rewriting every page at once, and the token layer gives the same
consistency without the churn. Accessible headless primitives
(`@radix-ui/react-dialog`, `-dropdown-menu`, `-tabs`, `-popover`) are added
only when the first screen needs a dialog, menu or tab set — hand-rolling those
correctly is not worth it.

### 6.2 Tokens

Defined in `app/tokens.css`. Values chosen from an 8-point scale and verified
for contrast.

Colour (light theme; dark theme is a later slice):

| Token | Value | Use | Contrast |
| --- | --- | --- | --- |
| `--color-canvas` | `#F5F6F5` | Page background | — |
| `--color-surface` | `#FFFFFF` | Panels, tables | — |
| `--color-fill` | `#EEF1EF` | Quiet tiles, table headers | — |
| `--color-text-strong` | `#1F2421` | Body and headings | 15.8:1 on surface |
| `--color-text-weak` | `#5B6660` | Secondary text | 6.0:1 surface, 5.5:1 canvas |
| `--color-stroke-strong` | `#848F89` | Form field borders | 3.4:1 surface |
| `--color-stroke-weak` | `#E1E6E2` | Dividers (decorative) | — |
| `--color-brand` | `#14532D` | Interactive only: buttons, links, current nav | 9.1:1 surface |
| `--color-brand-hover` | `#0F3F22` | Hover on brand | — |
| `--color-brand-fill` | `#E7F1EA` | Current nav item background | brand text 7.9:1 |
| `--color-error` / `-fill` | `#B42318` / `#FEF3F2` | Failed, blocked | 6.1:1 |
| `--color-warning` / `-fill` | `#93370D` / `#FFFAEB` | Needs review, late | 7.2:1 |
| `--color-success` / `-fill` | `#067647` / `#ECFDF3` | Healthy, done | 5.4:1 |
| `--color-info` / `-fill` | `#175CD3` / `#EFF8FF` | Shadow mode, informational | 5.6:1 |
| `--color-neutral` / `-fill` | `#475467` / `#F2F4F7` | Off, queued | 7.0:1 |
| `--color-sidebar` | `#16211B` | Sidebar background | text 13.7:1, weak text 6.7:1 |

Brand green is reserved for interactive elements. Status is never colour alone:
every status badge has an icon and a word.

Spacing: `--space-xs 8px`, `--space-s 16px`, `--space-m 24px`,
`--space-l 32px`, `--space-xl 48px`, `--space-xxl 80px`, plus `--space-2xs 4px`
for dense tables. Tighter spacing inside a group, larger between groups.

Type (ratio 1.2, base 16px, dense product):

| Token | Size / line height | Use |
| --- | --- | --- |
| `--text-h1` | 28 / 36 | Page title |
| `--text-h2` | 20 / 28 | Section heading |
| `--text-h3` | 16 / 24, bold | Panel heading |
| `--text-body` | 16 / 24 | Body |
| `--text-small` | 14 / 20 | Tables, metadata |
| `--text-caption` | 12 / 16 | Labels above values, group labels |

One typeface (Inter, with system fallbacks), two weights: 400 and 600.

Shape and depth: `--radius-s 6px`, `--radius-m 8px`, `--radius-l 12px`;
`--shadow-raised` for panels that float, `--shadow-overlay` for dialogs and
menus. Focus ring: 2px brand outline with 2px offset, on every interactive
element.

### 6.3 Components (`src/ui/`)

| Component | Slice | Notes |
| --- | --- | --- |
| `AppShell`, `SideNav` | 0 | Grouped, role-filtered, current item highlighted, environment band |
| `PageHeader` | 0 | Eyebrow (module), title, description, actions slot |
| `Button` | 0 | `primary` (one per screen), `secondary`, `tertiary`; 40px tall, Verb + Noun labels |
| `StatusBadge` | 0 | Maps any status string to a tone; icon + text |
| `Panel` | 0 | Surface with optional heading and actions |
| `EmptyState` | 0 | Headline, one line, optional action |
| `StatTile` | 1 | Label, value, supporting line, tone |
| `AttentionList` | 1 | Severity-ordered rows with one action each |
| `AutomationCard` | 1 | Name, mode, health, last and next run |
| `DataTable`, `FilterBar`, `Pagination` | 2 | Server-side sort and filter through search params |
| `Tabs`, `Timeline`, `KeyValueList` | 3 | Record template |
| `ModeSwitch`, `ConfirmDialog`, `Toast` | 2–3 | Off / Shadow / Live with confirmation into Live |
| `Stepper` | 3 | Task template |

### 6.4 Copy rules

Sentence case. Verb + noun on buttons ("Import statement", "Run now"). Errors
say what happened and what to do. Numbers as numerals. Dates as
`11 Nov 2026, 05:00` in South African time.

## 7. Build order

Each slice is one pull request, deployable on its own, with no page broken in
between.

| Slice | Scope | Done when |
| --- | --- | --- |
| **0 — Foundation** | Tokens, `AppShell` with grouped sidebar, production band, renamed product, core components, signed-out pages without navigation | Every existing page renders in the new shell; lint and build pass |
| **1 — Today** | New home: attention list across modules, automation tiles, key numbers | Home answers "what needs me" without opening another page |
| **2 — Automations** | `/automations` list and detail; Shoprite policy moves there; backend registry endpoint | Every automation shows mode, last and next run, with Run now |
| **3 — Banking module** | Overview per account, statement list, import as a Task page | Overdue accounts are visible; import flow uses the Task template |
| **4 — Shoprite module** | Routes under `/shoprite`, redirects, reference data under settings | Old URLs redirect; invoices use List and Record templates |
| **5 — Activity** | Runs list rebuilt on `DataTable`; Events merges messages and webhooks | One events stream with source filter |
| **6 — Web store stock** | Overview, shadow differences to review, product mapping | Lands with the stock sync backend |
| **7 — Reports** | Report index and a first report (statement coverage by account) | One report with filters and CSV export |
| **8 — Settings** | Connections health page, users moved | Each outside system shows reachable / last success |
| **9 — Dark theme, polish** | Dark token set, keyboard shortcuts, command palette | — |

Old CSS is removed page by page as each page moves to components; nothing is
deleted until no page uses it.

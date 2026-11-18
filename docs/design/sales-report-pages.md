# Design: sales report pages (R5)

Status: draft for goal R5, step 2.

The sales report pages replace the pivots of the sales workbook
`sales-report-new - 2026.xlsm`. The analysis of the workbook is in
`docs/design/reports-analysis.md`, sections 2 and 6. This design turns that
analysis into a build plan.

Source of the decision: the owner chose the sales report pages as the next
report (board decision `Lead/reports-next-goal`, 2026-11-18).

---

## 1. Data

All pages read the view `reporting.sell_in_report_lines`. The view applies the
rules of the old workbook (section 7a of the analysis):

- A sale counts in its posting period.
- Only invoices and credit memos count. Credit memos are negative.
- Items with no item class are left out.
- The rep and the customer group are the customer's current values. The view
  also gives the rep at the sale.

The nightly job loads production sales into the view at 02:00 UTC. The history
starts in March 2024 (financial year 2025). For a comparison with the year
before, a page can show financial years 2025 to 2027.

The parity of the view with the workbook is signed off: 515 of 524 totals per
month and rep are equal to the cent.

---

## 2. Each workbook pivot and its page

| Workbook sheet | What it shows | Page |
| --- | --- | --- |
| `Sales Rep (amount)` | Amount by rep > customer group > customer, by financial year and month | `/reports/sales/by-rep`, measure "Amount" |
| `Sales Rep (quantity)` | Quantity by warehouse > item group > product | `/reports/sales/by-rep`, measure "Quantity" (rows rep > item group > product) |
| `Products (amount)` | Quantity (the sheet name is wrong) by product > customer group > customer | `/reports/sales/by-product`, both measures |
| `Products (quantity)` | Quantity by item group > product > rep | `/reports/sales/by-product`, rows item group > product > rep |
| `Client Count` | Distinct customers by rep, by financial year and month | `/reports/sales/customers` |
| `Sheet1` to `Sheet8` | Drill-down extracts of the data | `/reports/sales/data`, with CSV export |
| (none) | The year so far against last year | `/reports/sales`, the overview |

The `Sales Rep (quantity)` pivot starts with the warehouse. The workbench page
starts with the rep, because each sheet of the workbook is used per rep. The
warehouse is a filter instead.

---

## 3. Pages

All pages use the templates and parts of `src/ui` only.

| Route | Template | Content |
| --- | --- | --- |
| `/reports/sales` | `DashboardPage` | `TabNav`: Overview, By rep, By product, Customers, Data. `StatTile`s: sales this financial year to date, the same months last year, and active customers. A `ColumnChart` by financial month, this year against last year |
| `/reports/sales/by-rep` | `ListPage` with a `PivotGrid` | Rows rep > customer group > customer. A `Tabs` switch between amount and quantity |
| `/reports/sales/by-product` | `ListPage` with a `PivotGrid` | Rows item group > product > customer group, or item group > product > rep |
| `/reports/sales/customers` | `ListPage` with a `PivotGrid` | Distinct customers by rep, by financial month |
| `/reports/sales/data` | `ListPage` with a `DataTable` | The sales lines, 100 for each page, with the same filters and a CSV export |

The navigation gets one item, "Sales report", under a new group "Insight". It
is for the roles Admin and Operator, as the other report pages are.

### 3.1 Filters

The filters live in the URL, so that a filtered report is a link that a person
can send.

- `FilterChips`: the channels of the channel table, and "All".
- `FilterBar`, at most 4 fields: financial years, rep, province, item group.
- The product, the warehouse and the document type are filters on the pages
  where they matter (By product, Data).

The default is the current financial year and the year before, all channels.

### 3.2 Channel

The workbook maps each customer group to a channel with a lookup list (31
rows). The table `reporting.customer_class_channels` holds this map. It is
empty now.

- A customer class with no row is in the channel "Other".
- A settings file, as for the statements, fills the table. The first file comes
  from the `lookups` sheet of the workbook. It stays outside the repository.

---

## 4. Report API

One endpoint gives each grid. The server groups the lines in SQL, so the
browser never receives all lines.

```
GET /api/reports/sales/{view}?years=2026,2027&channel=Example&rep=R01&province=GP&itemGroup=BARS&measure=amount
```

| View | Rows | Measures |
| --- | --- | --- |
| `by-rep` | rep > customer group > customer | amount, quantity |
| `by-product` | item group > product > customer group, or > rep (`then=rep`) | amount, quantity |
| `customers` | rep | distinct customers |
| `overview` | financial month | amount this year, amount last year, distinct customers |

The answer:

```json
{
  "columns": [{ "financialYear": 2027, "month": 1, "label": "Mar 2026" }],
  "rows": [
    { "level": 0, "key": "R01", "label": "Example Rep", "values": [1200.50], "total": 1200.50, "hasChildren": true }
  ],
  "totals": { "values": [1200.50], "total": 1200.50 },
  "dataTo": "2026-11-18T02:05:00Z"
}
```

- A level is open in the request (`open=R01,R01|RETAIL`). Closed levels send no
  children, so a page with 31 reps and 3 400 customers stays small.
- `format=csv` gives the same grid as a CSV file, with all levels open.
- `GET /api/reports/sales/data` gives the lines, 100 for each page, and CSV.
- `dataTo` is the end of the last successful sell-in run, for the freshness
  line on the page.
- The endpoints need the role Admin or Operator. They only read.

---

## 5. New parts in `src/ui`

Each part goes into `src/ui`, into `docs/design/design-system.md` and onto
`/ui-kit`, in the same change.

| Part | What it does |
| --- | --- |
| `PivotGrid` | A grid with a sticky first column, row levels that open and close, a total column, a totals row, and figures in tabular numbers. A row level opens with a link, so that it works without JavaScript and keeps the URL |
| `ColumnChart` | Columns by month, this year against last year. A small SVG part with no chart library, as the redesign document requires |

`DataTable` stays the table for lists. It allows 7 columns, and a year of
months needs 13 or more, so the report grids need `PivotGrid`.

---

## 6. Check

- **Tests:** the API tests use made-up lines. They check the grouping, the
  levels, the filters, the totals, the CSV, and the financial-year order of the
  months.
- **Parity:** the by-rep grid for financial years 2025 and 2026 must give the
  same total for each rep and month as the signed-off parity of R1. The check
  runs against production, read only, through the API.
- **Sign-off:** the owner compares the pages with the workbook and signs off.

---

## 7. Open questions

1. **Channel list.** The workbook has 3 channels. Are they still correct, and
   who keeps the map of customer groups to channels?
2. **Rep at the sale.** Must the by-rep page offer a switch to the rep at the
   sale? The view has it. The workbook does not.
3. **Access.** Must a rep see only their own customers? Question 6 of the
   analysis is still open. Until it is answered, Admin and Operator see all.

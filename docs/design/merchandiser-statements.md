# Design: monthly merchandiser statements (R3)

Status: draft for goal R3, step 1.

PVM pays four merchandiser companies for the sales in their area. Each month,
a person makes one statement for each company by hand, from pivots of the sales
workbook and from the Makro Dispo report. This design makes the same
statements in the workbench.

Source files (analysed on 2026-11-18, outside the repository):

| File | Company | Period |
| --- | --- | --- |
| `CMR PVM Oct 2026.xlsx` | CMR Meridian | October 2026 |
| `CoastCountry Oct 2026.xlsx` | Coast & Country (Western Cape) | October 2026 |
| `TWO OCEANS KZN Oct 2026.xlsx` | Two Oceans Marketing (KwaZulu-Natal) | October 2026 |
| `AHA ALCHEMY May - Sep 2026.xlsx` | Alchemy Agency | May to September 2026, and earlier periods |

This document holds no names of reps or customers and no amounts. It gives
the structure and the counts only.

---

## 1. What a statement is

All four statements follow one pattern:

```
Company
 └─ Area (1 to 3 for each company)
     ├─ Line: customer group, for a list of reps   → one amount
     │    └─ Detail: each customer of the line     → one amount each
     ├─ Line: Makro, for a list of Makro stores    → one amount
     ├─ Area total
     └─ Commission of the area (a rate), when the company is paid on commission
 ├─ Grand total of all areas
 └─ Commission on the grand total, or a fixed retainer
```

| Company | Areas | Lines in each area | Payment |
| --- | --- | --- | --- |
| CMR Meridian | 3: Gauteng, Free State, Eastern Cape | 3 customer groups and Makro | 5 % of each area and of the grand total |
| Coast & Country | 1: Western Cape | 3 customer groups and Makro | A fixed retainer of R 10 000 a month |
| Two Oceans | 1: KwaZulu-Natal | 4 customer groups and Makro | 5 % of the total |
| Alchemy Agency | 1: pharmacy distributors | 1 customer group | 10 % of the total |

### 1.1 The rule for a line, found from the data

The statements are pivots of the old sales workbook, filtered by rep. For
October 2026, the workbook data and the statements were compared:

- **Customer group × list of reps** gives the total of 15 of the 16 group
  lines of the first three companies to the cent.
- A rule by province gives wrong totals for 12 of the 16 lines (up to 7.5
  times too much). It is not used.
- One line is an exception: in CMR Gauteng, the Checkers line takes only the 2
  Checkers distribution centres, as its label "Checkers DC Gauteng & Freestate"
  says. So a line can also hold a fixed list of customers.
- Every customer amount in the detail sheets is equal to the workbook amount
  of that customer for the month, to the cent (227 customers).
- The statements shorten some names. Example: the customer "DIS-CHEM
  PHARMACIES LTD GARDEN ROUTE S304" shows as "DIS-CHEM GARDEN ROUTE S304".

The reps of a company are the PVM reps of its area. CMR Gauteng has 8 reps,
Coast & Country 3, and each of the other areas 1. The rep and the customer
group are the customer's current values, as in the sales report (R1, rule 4).

Alchemy Agency takes the customer group "Distributors Pharmacies" whole. The
group has 1 rep and 3 customers in 2026. Its statement amounts for August and
September 2026 are equal to the workbook amounts.

### 1.2 Makro

- The Makro line is the "Rand value" of a fixed list of Makro stores for the
  month. CMR has 15 stores over three areas, Coast & Country 3, and Two
  Oceans 4.
- The Rand value comes from the Makro Dispo report: Σ (units × net cost). The
  sell-out import (R2) keeps it as `ValueExclVat` in `reporting.sell_out_months`.
- The October values of all 22 stores match the import within 0.5 %. Refer to
  `docs/design/sell-out-import.md`, section 2.1.

### 1.3 Two Oceans: Dis-Chem year on year

- The Two Oceans file has an extra sheet. It shows the sell-in of the line
  "Dis-Chem KwaZulu-Natal" for each store, for the last 3 months. It also
  shows the same 3 months of the year before, with the growth in percent.
- This is sell-in from the sales report, not sell-out.
- The statement offers this sheet as an option of the company.

### 1.4 Periods

- A statement covers one month (CMR, Coast & Country, Two Oceans), or a range
  of months (Alchemy Agency: for example May to September).
- The month is the posting period, as in the sales report.

---

## 2. Settings

The settings say what each company gets. A person keeps them in the workbench.
Each setting has a "valid from" month, so that a change of area or rate does
not change an old statement.

| Table | Columns |
| --- | --- |
| `reporting.merchandiser_companies` | Id, name, statement title, payment kind (`commission` or `retainer`), rate in percent or fixed amount, Dis-Chem sheet (yes or no), valid from, valid to |
| `reporting.merchandiser_areas` | Id, company, name, order, commission rate (empty: no area commission) |
| `reporting.merchandiser_lines` | Id, area, label, order, customer class id, list of rep ids (empty: all reps), list of customer ids (empty: all customers of the group and reps) |
| `reporting.merchandiser_makro_stores` | Area, Makro store (from `reporting.retail_stores`) |

The first settings, for October 2026, come from the analysis in section 1.
They hold Acumatica ids: customer class ids, salesperson ids and customer ids.
A migration does not seed them. A person enters them on the settings page, or
a one-time import fills them from a file that stays outside the repository.

---

## 3. Data

| Part | Source |
| --- | --- |
| Customer group lines and details | The view `reporting.sell_in_report_lines` (R1): sum of `Amount` by customer, for the class, the reps and the months of the line |
| Makro line | `reporting.sell_out_months`, source `makro-dispo`: sum of `ValueExclVat` for the stores and the months |
| Dis-Chem sheet | `reporting.sell_in_report_lines` for the same line, this year and the year before |

A statement is computed when a person opens it. Nothing is stored, so a
statement always shows the data of the last load. An export keeps the numbers
of that moment.

A month with a partial Makro file (`IsPartial`) shows a warning on the
statement.

---

## 4. Workbench

- **Statements page:** choose a company and a month (or a range for a company
  that uses ranges). The page shows the summary, then each area with its lines
  and the customers under each line.
- **Export button:** an Excel file in today's layout: a sheet "Summary", one
  detail sheet for each area, a sheet "Makro", and for Two Oceans the Dis-Chem
  sheet. The totals and the commission are Excel formulas, as today, so that a
  person can follow them.
- **Settings page:** the companies, areas, lines and Makro stores.

The backend writes the Excel file with a small writer next to the existing
`XlsxReader`. No new package is necessary.

---

## 5. Check (step 6)

The QA copy of Acumatica is from June 2025, so it has no sales for October
2026. The check therefore runs in two parts:

1. **Engine check:** the statement engine runs on the workbook lines of
   October 2026 (extracted outside the repository), with the settings of
   section 2. Its output must equal the four hand-made files, line by line.
   Result on 2026-11-18: the engine gave all 21 values of the CMR, Coast &
   Country and Two Oceans statements for October 2026. The 16 group lines
   are equal to the cent. The 5 Makro totals are within 0.5 %, the
   difference of the Makro values (section 1.2). The settings came from the
   statements: 1 line needs a customer list, and each of the 15 CMR Makro
   stores belongs to one area.
2. **Live check:** when the sell-in load reads production, the workbench
   statement for October 2026 must give the same totals. This needs the
   inquiry PVM-Sales-Lines in production, which is a decision of the owner.

---

## 6. Open questions

1. **Production sell-in.** The live statements need the sell-in of production.
   May the owner import the inquiry PVM-Sales-Lines into production, and may
   the nightly load read it?
2. **Hand formulas.** In the CMR summary, the grand total formula of the last
   area adds the area totals by cell reference. The engine adds the area
   totals itself. If the totals of a hand-made file differ, the check reports
   the cell.
3. **Alchemy periods.** Does Alchemy Agency always get a range of months, and
   who chooses the range?

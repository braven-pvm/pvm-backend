# Acumatica Generic Inquiry: PVM-Sales-Lines

Status: built in QA (devtest1) on 2026-11-12 · Owner: Reports lane

`PVM-Sales-Lines` is the source of sell-in for the reports module. It returns one
row for each line of a released AR invoice, credit memo or debit memo. The
backend reads it over OData. The reports module does not read the Acumatica
database directly. Refer to
[`docs/design/reports-analysis.md`](../design/reports-analysis.md).

## Definition

Build it on screen SM208000 (Generic Inquiry). Tick "Expose via OData". The
caption of each result field becomes its OData field name, so the captions must
be exactly as below.

### Tables

| Data source | Alias |
| --- | --- |
| `PX.Objects.AR.ARTran` | `t` |
| `PX.Objects.AR.Customer` | `c` |
| `PX.Objects.AR.CustomerClass` | `cc` |
| `PX.Objects.CR.Address` | `a` |
| `PX.Objects.AR.SalesPerson` | `sp` |
| `PX.Objects.IN.InventoryItem` | `i` |
| `PX.Objects.IN.INItemClass` | `ic` |
| `PX.Objects.IN.INSite` | `w` |
| `PX.Objects.IN.INLocation` | `l` |

### Relations

| Parent | Join | Child | Parent field | Child field |
| --- | --- | --- | --- | --- |
| t | Inner | c | `customerID` | `bAccountID` |
| c | Left | cc | `customerClassID` | `customerClassID` |
| c | Left | a | `defAddressID` | `addressID` |
| t | Left | sp | `salesPersonID` | `salesPersonID` |
| t | Left | i | `inventoryID` | `inventoryID` |
| i | Left | ic | `itemClassID` | `itemClassID` |
| t | Left | w | `siteID` | `siteID` |
| t | Left | l | `locationID` | `locationID` |

### Conditions

`t.released` equals True, and `t.inventoryID` is not null, and `t.tranType` is
one of `INV`, `CRM`, `DRM`.

### Result fields

| Caption | Source | OData type |
| --- | --- | --- |
| `TranType` | `t.tranType` | String |
| `RefNbr` | `t.refNbr` | String |
| `LineNbr` | `t.lineNbr` | Int32 |
| `TranDate` | `t.tranDate` | DateTime |
| `TranPeriodID` | `t.tranPeriodID` | String |
| `FinPeriodID` | `t.finPeriodID` | String |
| `CustomerID` | `c.acctCD` | String |
| `CustomerName` | `c.acctName` | String |
| `CustomerClassID` | `cc.customerClassID` | String |
| `CustomerClassName` | `cc.descr` | String |
| `Province` | `a.state` | String |
| `SalesRepID` | `sp.salesPersonCD` | String |
| `SalesRepName` | `sp.descr` | String |
| `InventoryID` | `i.inventoryCD` | String |
| `ItemDescription` | `i.descr` | String |
| `ItemClassID` | `ic.itemClassCD` | String |
| `ItemClassName` | `ic.descr` | String |
| `WarehouseID` | `w.siteCD` | String |
| `WarehouseName` | `w.descr` | String |
| `LocationID` | `l.locationCD` | String |
| `LocationName` | `l.descr` | String |
| `Quantity` | `t.qty` | Decimal |
| `UOM` | `t.uOM` | String |
| `BaseQuantity` | `t.baseQty` | Decimal |
| `Amount` | `t.tranAmt` | Decimal |
| `LastModified` | `t.lastModifiedDateTime` | DateTime |

The QA inquiry also exposes `AddressID` and `Warehouse`. The backend does not
read them.

## How to read it

```
GET <base>/odata/<company>/PVM-Sales-Lines?$filter=TranPeriodID eq '012025'&$format=json
```

- Use Basic authentication as `<user>@<company>`. The OData service does not use
  the session cookie of the contract-based API.
- Filter by `TranPeriodID` in display format `MMYYYY`. `012025` is period 1 of
  financial year 2025, which is March 2024. The internal format `202501`
  returns no rows and no error.
- Read one period for each call. Do not call the inquiry without a filter, and
  do not depend on `$top`: an inquiry can compute all its rows before it applies
  `$top`.
- `Decimal` values come back as JSON strings. Parse them with the invariant
  culture.

## Facts measured in QA

Measured on 2026-11-12 for period `012025`. Counts only.

| Fact | Value |
| --- | --- |
| Time for one period | 13 s |
| Lines | 10 248 (9 713 invoice lines, 535 credit memo lines) |
| Document dates in the period | 1 to 25 March 2024. The period closes near the 25th |
| Distinct customers, items, reps, customer classes | 1 074, 97, 22, 55 |
| Lines with no province | 2 332 |
| Lines with no sales rep | 3 |
| Item class names | The 8 product groups of the sales workbook |
| Location names | The "type of good" values of the sales workbook |

## Export and import

The definition from QA is in
[`infra/acumatica/generic-inquiries/PVM-Sales-Lines.xml`](../../infra/acumatica/generic-inquiries/PVM-Sales-Lines.xml).
It is the export of screen SM208000. It holds the definition only, no data.

To put the inquiry in another instance, such as production:

1. Open screen SM208000 (Generic Inquiry) in that instance.
2. Use the import action on the toolbar, and choose the XML file.
3. Save. Make sure that "Expose via OData" is ticked.
4. Read one period over OData, as in [How to read it](#how-to-read-it).

The integration user needs only read access to the inquiry. If the inquiry
changes in QA, export it again on SM208000 and replace the file in the
repository.

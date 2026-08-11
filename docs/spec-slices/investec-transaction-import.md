# Investec Transaction Import → Acumatica Cash Management

Last updated: 2026-08-11
Branch: `feature/investec-transaction-import`

## Purpose & scope

A one-way feed that pulls **transaction history** from **Investec Business & Commercial
Banking (BCB)** and imports it into an existing **Acumatica Cash Management** cash account
so it can be reconciled against Acumatica's own payments and deposits.

In scope: transaction history only. **Out of scope** (explicitly dropped from the Investec
guide): payments initiation (pain.001), payment status (pain.002), balance enquiry, MT940.

Direction note: this is the *reverse* of the existing Acumatica invoice work — that reads
invoices *out* of Acumatica; this writes bank transactions *in*.

## Validated architecture (proven on QA)

Verified against the live QA tenant (`PVMGroup25R101D250625`, build 25.201, 2025 R2):

- The Default contract-REST endpoint (24.x and 25.x) exposes only `CashAccount` and
  `CashTransaction` — **no bank-statement / bank-feed entity**. So there is no out-of-box
  REST way to import statement lines, and `CashTransaction` is wrong here (it posts to GL
  and would double-count against Acumatica's own payments).
- The **Import Bank Transactions screen (CA306500)** is a real, insertable *document*:
  header = a bank statement per cash account; detail = transaction lines carrying
  `Ext. Tran. ID`, `Ext. Ref. Nbr.`, `Tran. Date`, `Tran. Desc`, `Receipt`, `Disbursement`.
- Therefore: a small **custom contract endpoint over CA306500** accepts a `PUT` of a
  statement + lines; Acumatica **de-duplicates natively on `Ext. Tran. ID`** and the lines
  flow into `CA306000` (Process Bank Transactions) matching → Reconciliation Statement.

Chosen over the native File Bank Feed (SFTP + CSV + Bank-Feed licence) because we already
build custom Acumatica REST integration and want API-to-API with no extra infra.

```
Investec  ── GET /za/bb/v2/accounts/{accountId}/transactions ──▶  PVM feed (this repo)
 (OAuth 2.0)   (date window + pagination)                          - map JSON → BankStatement
                                                                   - PUT to PVMBankFeed endpoint
Acumatica  ◀── PUT /entity/PVMBankFeed/01.000.001/BankStatement ───┘
  → dedup on Ext. Tran. ID → CA306000 match → Reconciliation Statement
```

## Acumatica endpoint spec (turnkey — for About IT Group / SM207060)

Create as a customization project so it deploys to production too.

1. **Extend Endpoint** off Default (latest, e.g. 24.200.001) → name `PVMBankFeed`,
   version `01.000.001`.
2. Top-level entity **`BankStatement`** → screen **`CA306500`**, header fields:
   `CashAccount`, `StatementDate`, `StartBalanceDate`, `EndBalanceDate`,
   `BeginBalance`, `EndBalance`.
3. Detail entity **`Details`** → line fields: **`ExtTranID`** (dedup key, required),
   `ExtRefNbr`, `TranDate`, `TranDesc`, `ReceiptAmount`, `DisbursementAmount`
   (optional: `CardNumber`, `InvoiceNbr`).
4. Save (publishes). Consumed at `/entity/PVMBankFeed/01.000.001/BankStatement`.

## Data model & mapping

Canonical import model (`Pvm.Application.Banking.BankStatementImport` + `BankStatementLine`)
maps 1:1 to the endpoint above. Transform lives in
`Pvm.Application.Banking.InvestecBankStatementMapper`.

| Acumatica line field | Source |
| --- | --- |
| `ExtTranID` | Investec stable transaction id **if present**, else a deterministic hash of `accountId·bookingDate·signedAmount·runningBalance·description` (running balance disambiguates identical same-day amounts and keeps the id stable across overlapping re-pulls) |
| `TranDate` | Investec `postingDate ?? transactionDate` |
| `ReceiptAmount` / `DisbursementAmount` | Split by direction: CREDIT/positive → Receipt, DEBIT/negative → Disbursement |
| `TranDesc` | Investec `description` |
| `ExtRefNbr` / `CardNumber` | Investec `reference` / `cardNumber` |

Statement header: one statement per cash account per pull window; `StatementDate` /
`EndBalanceDate` = window end; balances derived from Investec running balances when present.

## Idempotency

`Ext. Tran. ID` is the whole idempotency contract — Acumatica skips re-imported ids. Our
feed pulls **overlapping** windows for completeness and relies on this dedup, same posture
as the Acumatica invoice reconciliation.

## Open questions (confirm before go-live)

1. **Investec response schema** — the guide has no JSON response shapes (they're on the
   Developer Portal / sandbox). The `InvestecTransaction` DTO is modelled from the guide +
   known Investec structure and must be verified against the sandbox, especially whether
   `amount` is signed or magnitude+direction, and field names.
2. **Stable transaction id** — does the BCB API return a unique per-transaction id? If not,
   we rely on the deterministic hash (already implemented as the fallback).
3. **Cash account** — which Acumatica Cash Account CD is the Investec account (the import
   target). `CashAccount` was not GET-exposed on Default 24.x; confirm the CD in-tenant.
4. **OAuth credentials & `accountId`** — client key/secret from the Investec Integration
   Manager; the system-assigned `accountId` (2–3 digits) for the transactions endpoint.
5. **Trigger** — scheduled (reuse the Container Apps scheduler + integration-run pattern)
   vs. manual-first. Proposed: manual refresh first, then scheduled.

## Acceptance criteria

- Operator/schedule pulls Investec transactions for the configured account + window.
- Each transaction maps to a statement line with correct Receipt/Disbursement and a stable
  `Ext. Tran. ID`.
- `PUT` to `PVMBankFeed` creates a statement; re-running an overlapping window imports **no
  duplicate lines** (native dedup).
- Imported lines appear on CA306000 and can be matched/reconciled against Acumatica
  payments/deposits on the existing cash account.

## Build status

- Done: canonical model, Investec source DTO/contract, `InvestecBankStatementMapper` +
  unit tests (`Pvm.Application.Tests/Banking`).
- Next: `InvestecTransactionClient` (OAuth + paging), `IAcumaticaBankStatementClient` +
  `AcumaticaBankStatementClient` (PUT to PVMBankFeed), DI wiring, scheduled refresh, and a
  live QA insert+dedup proof once the endpoint exists.

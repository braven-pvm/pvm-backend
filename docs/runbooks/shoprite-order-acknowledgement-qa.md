# Shoprite Layer 7 Headers and Order Acknowledgement QA Runbook

> **The integration no longer reads Shoprite orders at all. Do not switch
> acknowledgement or order reading on.**
>
> On 2026-10-08 this runbook's earlier assumption was proven wrong. It said a
> portal acceptance changes the order status at Shoprite, so an API
> acknowledgement was redundant. The evidence:
>
> - Shoprite has two queues. Portal processing by PVM's Manual users does not
>   clear the Auto Download (API) queue. Sales orders existed for orders that
>   the API still offered as new days later.
> - Reading an order through the API does change what Manual users see. Shoprite
>   auto-acknowledges an order about 118 minutes after the API fetches it without
>   acknowledging, and PVM staff found their orders already acknowledged.
> - Shoprite (Randi Walters, 2026-10-08) confirmed that any download or view moves
>   an order from Current to Auto Downloaded/Viewed, and that no read avoids it.
>
> Shoprite's designed EDI process is the reverse of PVM's: the system downloads
> orders and Manual users read them under Old. PVM's people work orders in
> Current, so the integration stays out of the order channel.
>
> Invoices are now completed from Shoprite reference data instead of the order:
> `shoprite_delivery_locations` (Acumatica customer = Shoprite branch → GLN) and
> `shoprite_trade_items` (Acumatica item and unit → Shoprite GTIN, per store or
> distribution centre). Order reading sits behind `ShopritePoRefresh:Enabled`,
> off by default, and the scheduled job and its staleness alert exist only when
> `shopritePoRefreshEnabled` is true.

Purpose: verify the two production requirements that Shoprite confirmed on
2026-08-24.

1. Every call carries the Layer 7 headers. Shoprite states that a call fails
   without `Authorization`, `ContractID`, and `UIUser`.
2. Downloaded orders are acknowledged. Shoprite returns the same orders, and
   stops providing new orders, until acknowledgement succeeds.

## Two Shoprite hosts, two authentication styles

Shoprite runs two interfaces, and they do not accept the same request.

| Host | Used by | Authentication |
|---|---|---|
| `b2b.shopriteholdingsqa.co.za/B2BWebAPISupplierServices/api` | Our QA integration | Query-string credentials only |
| `externalservices.shopriteholdings.co.za/b2bservice/api` | Production | Layer 7 headers |

Verified on 2026-08-31: the QA supplier-services host answers `HTTP 200` with JSON
when the credentials are in the query string, and answers `HTTP 302` with an
empty body when the request carries an `Authorization` header. The redirect
target is not JSON, which is why an unconditional Layer 7 header broke the QA
order refresh.

The documented QA gateway
`externalservicesqa.shopriteholdings.co.za/b2bservice/api` answers `HTTP 401`
`SH-401-EXT Authentication Required` with the current QA credentials and the
contract identifier from the guide. Ask Shoprite to enable the QA account on
that gateway. Until they do, the Layer 7 header path cannot be proven before
production.

`Shoprite:UseLayer7Headers` selects the style. It is `false` in QA and `true` in
production.

## Configuration

| Setting | QA value | Where |
|---|---|---|
| `Shoprite__UseLayer7Headers` | `false` | Deployment parameter |
| `Shoprite__ContractId` | `aa659aa2-4175-471f-8c82-59ca416723cf` | Key Vault secret `shoprite--contractid` |
| `Shoprite__UiUser` | Not set. Defaults to the username | Optional container setting |
| `Shoprite__AcknowledgeOrders` | `true` | Deployment parameter |

Shoprite uses the same contract identifier for QA and production.

## Test 1: The refresh still works

1. Trigger a PO refresh from **Messages**.
2. Confirm the run succeeds under `/runs`.

Pass: the refresh returns orders. In QA this proves that no Layer 7 header is
sent, because the supplier-services host redirects any request that carries one.

## What may be acknowledged

Only the orders that Shoprite returned in the current fetch. Shoprite rejects
the whole call when the list contains an order the account no longer holds:

```
Current User PVMQA does not appear to have any of the orders in the given list,
Order numbers: 1212021109
```

An order that was downloaded before acknowledgement existed is therefore never
acknowledged. It stays without an acknowledgement time, and that is correct.
Shoprite re-offers an unacknowledged order in the next fetch, so a failed
attempt is retried by the protocol itself.

Observed on the QA host on 2026-08-31:

- `PUT VendorOrder?action=A` with a JSON array of order numbers is the right
  shape. Removing `action` returns `405`.
- An order the account no longer holds returns `500` with the message above.

## Test 2: Orders are acknowledged once

1. Run a PO refresh.
2. Query the database:

```sql
select count(*) filter (where "AcknowledgedAt" is null) as pending,
       count(*) filter (where "AcknowledgedAt" is not null) as acknowledged,
       max("LastAcknowledgementError") as last_error
from shoprite_purchase_orders
where "FirstSeenAt" > now() - interval '1 day';
```

Pass: every order received today is acknowledged, with one attempt and no
error. A second refresh acknowledges nothing more. Orders received before this
feature existed stay unacknowledged, which is expected.

## Test 3: A failed acknowledgement is safe

1. Set `Shoprite__ContractId` to an invalid value on the worker revision.
2. Run a PO refresh.

Pass: the run still completes and stores orders. The orders stay pending, each
records an attempt and an error, and the log contains
`shoprite.order.acknowledgement.failed`. Restore the setting, run the refresh
again, and confirm the same orders are acknowledged.

## Test 4: New orders keep arriving

1. Ask Shoprite to place a new QA order, or wait for the next order batch.
2. Run a PO refresh.

Pass: the new order appears. This proves acknowledgement did not stop the feed.

## Test 5: Reset returns orders for download

Caution: reset makes Shoprite offer the orders again. Use it only for support.

1. Call the Admin endpoint:

```bash
POST /api/shoprite/purchase-orders/reset
{"purchaseOrderNumbers":["1212021109"],"reason":"QA reset verification"}
```

2. Run a PO refresh.

Pass: the endpoint records a `shoprite-orders-reset` audit event with the actor
and the reason, the local order returns to pending, and the next refresh
receives the order again.

## Rollback

Set `Shoprite__AcknowledgeOrders` to `false` and redeploy. The integration then
stores orders without acknowledging them, which is the behaviour before this
change. Orders already acknowledged stay acknowledged at Shoprite until they are
reset.

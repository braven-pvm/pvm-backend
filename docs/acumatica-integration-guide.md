# Acumatica Integration Guide

A working reference for integrating a new Acumatica instance. Written from a
production integration that moves bank statements and inventory between
Acumatica, banks and a web store. Every rule here comes from something that
broke.

## Before you start

Gather four things. Verify each one against the live site before you write any
code, because three of the four are commonly wrong on the first attempt.

| What | How to get it | The trap |
| --- | --- | --- |
| **Base URL** | The site root only | The browser URL is not the base URL |
| **Company code** | The `CompanyID` query parameter in the browser URL | Often differs from the tenant or database name |
| **Endpoint name and version** | Screen SM207060, or default to `Default` and the version shown there | Version strings look like `24.200.001` and are not the Acumatica release number |
| **Integration user** | Created by whoever administers the instance | It needs explicit form rights, which it will not have by default |

**The base URL is the part people get wrong.** A browser sitting on an
Acumatica page shows something like
`https://site.example.com/(W(9))/Main?CompanyID=ACME`. The `(W(9))` is a
cookieless session token and `Main` is a screen. Neither belongs in an API
call. The REST base is the site root:

```
Browser:  https://site.example.com/(W(9))/Main?CompanyID=ACME
API base: https://site.example.com
Company:  ACME
```

Some instances are hosted under a virtual directory, in which case that path
segment is part of the base: `https://host.example.com/InstanceName`. Tell the
two apart by the first segment. A `(W(n))` segment is a session token and is
discarded; anything else is a real virtual directory and is kept.

Verify the whole set in one call before going further. If this returns 204, all
four values are right:

```bash
curl -sS -c cookies.txt \
  -H 'Content-Type: application/json' \
  -d '{"name":"<user>","password":"<pass>","company":"<company>"}' \
  "<base>/entity/auth/login" -w '%{http_code}\n'
```

## Authentication

The contract REST API uses a **session cookie**, not a bearer token. You sign
in, you make calls, you sign out. Treat the session as a scarce resource:
Acumatica licences limit concurrent API sessions, so always sign out, even
after a failure.

```bash
# 1. sign in -> 204, with a Set-Cookie header
curl -sS -c cookies.txt -H 'Content-Type: application/json' \
  -d '{"name":"<user>","password":"<pass>","company":"<company>"}' \
  "<base>/entity/auth/login"

# 2. call, carrying the cookie
curl -sS -b cookies.txt "<base>/entity/Default/24.200.001/StockItem?\$top=1"

# 3. sign out -- note the empty body
curl -sS -b cookies.txt -X POST --data '' "<base>/entity/auth/logout"
```

`branch` is accepted alongside `company` when an instance needs it. Omit it
otherwise.

### Failures and what they mean

| Response | Meaning |
| --- | --- |
| `204` on login | Success. There is no body; the session is in the cookie |
| `401` | Wrong credentials, or the account cannot use the API |
| `403` on a later call | Signed in, but the user lacks rights to that form |
| `411 Length Required` on logout | The logout POST had no body. Send an empty one |
| `500` mentioning a password change | The account has *force password change at next login* set |

Three account settings break an integration user, and none of them is obvious
from the error:

- **Force password change at next login must be off.** It blocks API sign-in
  outright.
- **The password should not expire**, or the feed stops silently on expiry day.
  If your policy forbids that, schedule the rotation.
- **The account must authenticate with a password.** An account that only signs
  in through single sign-on cannot use this endpoint.

In code, manage the cookie yourself rather than relying on a handler's cookie
jar, and always sign out in a `finally` block. One session per run is enough;
do not sign in per request.

## Choosing how to read data

Start with the Default endpoint. Move up only when it cannot answer.

| Path | Use it when | Cost |
| --- | --- | --- |
| **Default endpoint** | The data maps to a standard entity such as `StockItem` or `SalesOrder` | None. It already exists |
| **Generic Inquiry** | You need a joined or calculated result, or a screen the entities do not expose | Build the inquiry in the instance, then read it over OData |
| **Custom endpoint** | You need to **write** to a screen the Default endpoint does not cover | Build and map it, then package it to move between instances |
| **Direct SQL Server** | Never | See below |

### Do not query the database directly

It is tempting, especially when inheriting a script that already does it.
Resist it for three reasons, each of which we hit in practice.

1. **It breaks on upgrade.** Acumatica renames and restructures tables between
   releases. A query written against `INLocationStatusByCostCenter` is a time
   bomb, and you will not learn it has gone off until stock is wrong.
2. **The port is usually firewalled.** Port 1433 is routinely closed to
   everything but the office network, while 443 is open. We spent a full round
   trip with a hosting provider asking them to open 1433, before establishing
   that the REST API on 443 had been reachable the entire time.
3. **It needs database credentials**, which are far more dangerous than an API
   account scoped to a few forms.

If you inherit a working script that uses direct SQL, the migration is usually
smaller than it looks. Read its queries, find the equivalent entity or inquiry,
and check what the API already gives you before you plan anything. In our case
every figure a 200-line stock query computed was available from three standard
entities.

### Check what you already have

Before designing anything, ask the instance. A 403 is a good answer: it means
the entity exists and you only need rights.

```bash
for E in StockItem SalesOrder InventoryQuantityAvailable Warehouse; do
  printf '%-28s ' "$E"
  curl -sS -b cookies.txt "<base>/entity/Default/24.200.001/$E?\$top=1" \
    -w 'HTTP=%{http_code}\n' -o /dev/null
done
```

## Permissions

Acumatica authorises by **form**, not by entity. An endpoint entity is a view
onto a screen, so the user needs rights to that screen. This is the single most
common reason a correct integration returns 403.

### Let the error tell you what to ask for

A 403 from the contract API names the form and its screen ID. Do not guess;
read it.

```json
{ "message": "You have insufficient rights to access the InventoryQuantityAvailable (SO640590) form." }
```

That is the exact text to put in a request to whoever administers the instance.
Probe every entity you intend to use, collect the screen IDs, and ask once.
Asking in three instalments is how a one-day task becomes a one-week task.

### Screen IDs are stable, titles are not

A site can rename a screen; the ID never changes. Always identify a form by ID.
Some useful ones:

| Screen ID | Form |
| --- | --- |
| SM201010 | Users |
| SM207060 | Web Service Endpoints |
| SM204505 | Customization Projects |
| CA202000 | Cash Accounts |
| CA306500 | Import Bank Transactions |
| IN202500 | Stock Items |
| IN204000 | Warehouses |
| IN401000 | Inventory Summary |
| IN402000 | Inventory Allocation Details |
| SO301000 | Sales Orders |
| SO640590 | Inventory Quantity Available |

### What to ask for

Request read-only rights unless the integration writes. Name the screens by ID,
and say which need insert or edit. A worked example:

> Please create a dedicated API service account for company `ACME`, using
> password authentication, with the password set not to expire and no forced
> change at next login. Grant it a role with **Insert and Edit** on CA306500
> (Import Bank Transactions) and **View** on CA202000 (Cash Accounts), and add
> it to any restriction group covering cash accounts `X` and `Y`. Please
> confirm the licence has an API user seat.

**Restriction groups are a separate gate.** Rights to a form do not grant
visibility of the records on it. If the instance restricts cash accounts,
warehouses or branches by group, the integration user must be in the right
group or it will see an empty list and no error at all. That failure is silent,
which makes it worth asking about up front.

## Building a custom endpoint

You need one when you must write to a screen the Default endpoint does not
expose. Build it on **SM207060 (Web Service Endpoints)**. This is not an
administrator-only task; you can do it yourself with normal access to that
screen.

1. Extend the Default endpoint. Give the new endpoint a name and a version such
   as `01.000.001`, and record the base it extends.
2. Add a **top-level entity** and map it to the screen's header object.
3. Add a **detail entity** for line items, typed as an array, mapped to the
   screen's detail object.
4. Add and map the fields you need on each.
5. Save, then confirm with `GET <base>/entity/<Name>/<Version>/<Entity>`.

### Get the field names from the endpoint, not from the screen

The endpoint's field names are the ones you map, and they often differ from the
screen labels and from what you would guess. On the bank statement screen the
fields turned out to be `Receipt` and `Disbursement`, not `ReceiptAmount` and
`DisbursementAmount`, and `BeginningBalance` and `EndingBalance`, not
`BeginBalance` and `EndBalance`. Every one of those guesses cost a failed round
trip.

Read the truth out of the exported project XML, which lists every field and its
mapping:

```xml
<TopLevelEntity name="BankStatement" screen="CA306500">
  <Fields>
    <Field name="CashAccount" type="StringValue" />
    <Field name="Details" type="Details[]" />
  </Fields>
  <Mappings>
    <Mapping field="CashAccount"><To object="Header" field="CashAccountID" /></Mapping>
  </Mappings>
</TopLevelEntity>
```

Header fields go on the top-level entity and line fields go on the detail
child. Putting a line field on the header produces a payload Acumatica accepts
and quietly ignores.

### If you are driving the screen through browser automation

The endpoint screen is an Aurelia application inside an iframe, and it resists
synthetic events. Two limits are worth knowing before you spend an hour on
them:

- **Grid cells cannot be filled synthetically.** The roaming cell editor only
  activates on a real, trusted mouse click. Dispatched `MouseEvent` sequences
  do not move it.
- **The POPULATE action only fires on a real click.** So does the lookup
  selector's magnifier, although that one does respond to a full `mousedown` +
  `mouseup` + `click` sequence dispatched on the control icon.

Ask a person to perform those specific clicks rather than trying to automate
around them.

## Moving configuration between instances

Build the endpoint once, then carry it to every other instance as a
**customization project**. Do not rebuild it by hand per site; the field names
will drift and you will not notice until a write silently drops a value.

### Export from the source instance

1. Open **SM204505 (Customization Projects)**.
2. Click **`+`** to add a row, type the project name in the **Project Name**
   cell, and **Save**.
3. Click the project name to open the **Customization Project Editor**.
4. From the **Add** menu, choose **Web Service Endpoints**, tick your endpoint,
   and **Add**.
5. Choose **File > Export Project Package**. A `.zip` downloads.

The export lives under **File**, not on a toolbar button. The Source Control
menu's *Save Project to Folder* is a different feature; it is not what you
want.

Publishing in the source instance is **not** required before exporting.

### Verify the package before you ship it

The `.zip` contains a single `project.xml`. Read it. It is small, and it is the
authoritative record of every field and mapping. A clean single-endpoint
package looks like this:

```
project.xml  (~5 KB)
  <Customization product-version="25.201">
    <EntityEndpoint>
      <Endpoint name="..." version="01.000.001">
        <ExtendsEndpoint name="Default" version="20.200.001" />
```

Confirm it contains only what you meant to move. Anything else in there will be
applied to the destination instance too.

### Import into the destination

1. Open **SM204505** on the destination.
2. **Import** the `.zip`, open the project, then **Publish**.
3. Confirm with `GET <base>/entity/<Name>/<Version>/<Entity>`. A 404 means it
   did not publish; a 403 means it published and the user lacks rights.

**Publishing recycles the Acumatica application**, so the site is briefly
unavailable. Do it in a quiet window.

Note the version pairing: the package records the base endpoint version it
extends, such as `Default 20.200.001`. That base must exist on the destination.
It usually does, but it is the first thing to check if a publish fails.

## Writing data safely

**Acumatica does not de-duplicate a contract API import.** This surprises
people because several screens advertise duplicate protection. That protection
belongs to the built-in bank feed, not to your endpoint. A repeated `PUT`
creates a second document with the same lines, and nothing warns you.

So idempotency is your job. The pattern:

1. Read back the external identifiers already present for the target account
   and date window.
2. Drop every line whose identifier already exists.
3. If nothing is new, write nothing at all. Do not post an empty document.
4. Recompute any derived totals for the filtered set, so opening and closing
   figures still agree.
5. Fail open. If the read-back errors, do not block the import, but log it
   loudly.

### Never trust a source identifier until you have tested it

This is the rule that matters most, and it cost us a near-miss with real money.

We assumed a bank's OFX `FITID` was unique, and nearly used it as the external
identifier. It is not. The same bank reuses identifiers across statements:

| Identifier | In one statement | In the next |
| --- | --- | --- |
| `00000677600` | Deposit refund, +44 174.88 | Card payment, +5 474.00 |
| `00000677602` | Transfer out, -20 000.00 | Card payment, +1 915.90 |

Had we used it, the duplicate check would have matched five of six genuine
transactions as already imported and **silently discarded them**. A different
source, a bank's own API, turned out to be perfectly unique: 51 transactions,
51 distinct date-prefixed identifiers.

The two looked equally trustworthy. Only measurement told them apart.

**So measure.** Pull a realistic sample spanning several source documents, and
count:

```python
ids = [t["id"] for t in sample]
print(len(ids), len(set(ids)))   # must be equal, across documents, not within one
```

If they are not unique, derive your own: hash the transaction's content, prefix
it with a source tag, and keep the original identifier in a reference field so
a human can still trace it.

### One writer per record, per period

If a person already imports this data by hand, their identifiers will not match
yours. Neither side's duplicate check can see the other, so any overlap
double-posts.

Pick a cutover date and let exactly one path own each period. Then **derive the
current coverage from the detail rows, not the document header**. We found
headers that lag their contents badly: a statement dated 14 September contained
only lines dated 11 September. Trusting the header would have orphaned three
days of transactions that neither path would ever import.

## Querying

OData support is partial. Test each option against the instance rather than
assuming, because the failures are not graceful.

| Option | Works | Note |
| --- | --- | --- |
| `$filter` on a string field | Yes | `$filter=CashAccount eq 'ACME'` |
| `$filter` on a date field | **No** | Returns HTTP 500, not a 400. Filter dates in your own code |
| `$orderby` | Yes | `$orderby=EndBalanceDate desc` |
| `$top` | Yes | Always set it. See below |
| `$expand` | Yes | Detail rows are **absent** unless expanded |
| `$select` | Varies | Test it; do not depend on it |

Three things to internalise:

**Omitting `$top` returns everything.** Not a page, everything. One account in
our system held 332 documents, each with its detail rows. A missing `$top`
turns a quick lookup into a multi-megabyte response and a slow one.

**Detail rows need `$expand`.** Without it the child array comes back empty,
which reads exactly like a document with no lines. This is an easy way to
conclude that data is missing when it is merely unexpanded.

**Server-side date filtering fails with a 500.** That status suggests a server
fault and sends you debugging the wrong thing. It is simply unsupported. Order
by the date descending, take a bounded `$top`, and apply the window in your own
code:

```
?$filter=CashAccount eq 'ACME'&$orderby=EndBalanceDate desc&$expand=Details&$top=100
```

That shape is worth memorising: filter on a string, order by date, expand the
children, cap the count, then window client-side.

## Network and secrets

### Test reachability from where the code will run

Not from your laptop. An office network is usually allowlisted; a cloud
environment is usually not. Test from inside the runtime:

```bash
bash -c '(exec<>/dev/tcp/<host>/443) && echo OPEN || echo BLOCKED'
```

A connection that **times out** rather than being refused is the signature of a
firewall dropping packets, which means source-address filtering. A refusal
means nothing is listening.

Before concluding it is an allowlist, prove general egress works. If the
runtime validates tokens against an identity provider, it already reaches the
internet, and the block is specific to that host.

### Find your real outbound address by measuring it

This one cost us a wasted round trip with a hosting provider. We gave them the
address from our platform's configuration, which was the **inbound** address.
Outbound traffic left from a different one, so the allowlist they added did
nothing.

Ask the network, never the configuration:

```bash
curl -sS https://api.ipify.org     # from inside the runtime
```

Then check the assumption behind the request at all. We eventually discovered
port 443 was not filtered at all, and only the database port was. A control
test settles it: call the host from an environment you know is **not** on the
allowlist. If it succeeds, the port is open to everyone and no allowlist is
needed.

Cloud outbound addresses can also change unless you have pinned them with a
dedicated gateway. If an integration depends on an allowlist, that drift will
break it silently one day.

### Secrets

Credentials belong in a secret store, read at startup. Never in a repository.

We found an inherited script with database and store credentials in a
`config.json` committed across four commits, on a database whose port accepted
connections from the public internet. Those two facts together mean anyone with
that file owns the ERP.

- Check `git log -- <config file>` on any script you inherit, before anything
  else.
- Rotate anything that has been committed. Removing the file does not remove it
  from history.
- Prefer closing a port to allowlisting it. If an integration can use the REST
  API on 443 instead, ask for the database port to be **closed**, not opened.

When you print diagnostics, print lengths and identifiers, never values.

## Scheduling and operations

Run an integration as a scheduled job, not a script on somebody's desktop. A
desktop script has no logs anybody else can read, stops when the machine
sleeps, and keeps its credentials in a file.

### Window design

Overlap the window deliberately. A daily pull that reads the last seven days
collects anything a failed run missed, and costs nothing because the duplicate
check discards what is already present. Self-healing beats alerting for this
class of job.

Then put a **floor** under the window. A lookback that reaches back past the
cutover date will re-import the period a person already imported by hand, under
identifiers that cannot match. Make the floor a configuration value, pin it per
environment, and unit-test the clamp. Ours reads:

```
from = max(today - lookbackDays, feedStartDate)
to   = today
```

### Behaviour a job needs

- **Write nothing when the window is empty.** A run that finds no source
  records should log and exit, not post an empty document.
- **Fail loudly.** Rethrow after logging, so the scheduler records a failed run.
  A job that swallows its errors and reports success is worse than no job.
- **Log one structured line per run** with the window, how many records were
  read, how many were written, and the resulting document reference. That
  single line answers most questions without a debugger.
- **Cap concurrency.** One thread per record is a habit from small scripts; at
  scale it is a denial-of-service attack on your own store.

### When a run fails

Check in this order. It is roughly the order of likelihood.

1. **Read the exception, not the symptom.** A `SocketException` on connect is a
   firewall, not an authentication problem.
2. **Confirm sign-in still works.** Passwords expire, accounts get disabled.
3. **Re-probe rights.** A role change elsewhere can revoke a form.
4. **Check reachability from the runtime**, not from your machine.
5. **Compare what the source holds against what the target holds**, by
   detail-row date, before concluding anything is lost.

## Traps checklist

Each of these cost real time. The symptom rarely points at the cause.

| Symptom | Cause | Fix |
| --- | --- | --- |
| API calls fail against a URL copied from the browser | `(W(n))` session token and `Main?CompanyID=` are in the URL | Use the site root; company goes in the login body |
| `411 Length Required` on sign-out | The logout POST had no body | Send an empty body |
| `403` on an entity that clearly exists | The user lacks rights to the underlying **form** | Read the screen ID out of the 403 message and request it |
| A query returns an empty list, no error | A restriction group hides the records | Add the user to the group |
| Detail rows come back empty | `$expand` was omitted | Add `$expand=<Detail>` |
| A date filter returns HTTP 500 | Server-side date filtering is unsupported | Order by date, cap with `$top`, window in code |
| A simple lookup is enormous and slow | `$top` was omitted, so everything returned | Always set `$top` |
| A re-run creates a duplicate document | Acumatica does not de-duplicate contract API imports | Read back identifiers and filter before writing |
| Genuine records silently vanish on import | A source identifier that repeats was used as the key | Measure uniqueness across documents; hash the content if it repeats |
| Days of data belong to neither system | The cutover was set from document **header** dates | Derive coverage from detail-row dates |
| Fields are accepted but ignored | A line field was placed on the header entity | Header fields on the top level, line fields on the detail child |
| A field name guess fails | Endpoint names differ from screen labels | Read them from the exported `project.xml` |
| A vendor allowlists an address and nothing changes | The address given was the **inbound** one | Measure the outbound address from inside the runtime |
| A grid cell will not accept typed text | The screen is Aurelia; the cell editor needs a real click | Ask a person to do that step |
| An integration works from the office and not from the cloud | Source-address filtering | Test from the runtime; verify with a known-unlisted environment |

### The two habits behind most of these

**Measure, do not assume.** Identifier uniqueness, outbound addresses, field
names, port reachability and data coverage all looked obvious and all were
wrong. Each was one command away from certain.

**Read the error text.** Acumatica's 403 names the exact form. The socket
exception names the exact host and port. The migration output names every
applied migration. These errors are unusually informative, and the time lost
was mostly time spent theorising instead of reading them.

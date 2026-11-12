// A local stand-in for the backoffice API, for UI work only.
//
//   node scripts/mock-api.mjs --port 59999
//
// Point the workbench at it with NEXT_PUBLIC_API_BASE_URL=http://127.0.0.1:59999
// and AUTH_MODE=DevelopmentBypass. Every response is example data in the shape
// of src/api/client.ts. Writes answer with success and change nothing.
// set MOCK_EMPTY=1 in the environment of this script.

import { createServer } from "node:http";

const portIndex = process.argv.indexOf("--port");
const port = portIndex > 0 ? Number(process.argv[portIndex + 1]) : 5000;
const empty = process.env.MOCK_EMPTY === "1";

const now = Date.now();
const minutesAgo = (minutes) => new Date(now - minutes * 60_000).toISOString();
const daysAgo = (days) => minutesAgo(days * 24 * 60);

const runs = [
  run("6f1d0c2a-0001-4a7e-9a10-000000000001", "AcumaticaInvoiceDiscovery", "Schedule", "Succeeded", 8, { receivedCount: 3, createdCount: 1, updatedCount: 2 }),
  run("6f1d0c2a-0002-4a7e-9a10-000000000002", "AcumaticaInvoiceReconciliation", "Schedule", "Succeeded", 18, { receivedCount: 12, unchangedCount: 12 }),
  run("6f1d0c2a-0003-4a7e-9a10-000000000003", "InvestecStatementFeed", "Schedule", "Failed", 340, { errorCode: "AcumaticaSignInTimeout", errorSummary: "Acumatica sign-in timed out after 100 s." }),
  run("6f1d0c2a-0004-4a7e-9a10-000000000004", "AcumaticaInvoiceLookback", "Schedule", "Succeeded", 520, { receivedCount: 41, unchangedCount: 41 }),
  run("6f1d0c2a-0005-4a7e-9a10-000000000005", "ShopritePurchaseOrderRefresh", "Manual", "Running", 2, {}),
];

function run(id, runType, trigger, status, startedMinutesAgo, counts) {
  return {
    id,
    runType,
    trigger,
    initiatedBy: trigger === "Manual" ? "developer@pvm.co.za" : "scheduler",
    environmentName: "Production",
    correlationId: `corr-${id.slice(-4)}`,
    messageId: `msg-${id.slice(-4)}`,
    status,
    attemptCount: status === "Failed" ? 3 : 1,
    receivedCount: 0,
    createdCount: 0,
    updatedCount: 0,
    unchangedCount: 0,
    skippedCount: 0,
    revalidatedCount: 0,
    failedCount: status === "Failed" ? 1 : 0,
    errorCode: null,
    errorSummary: null,
    cursorBefore: minutesAgo(startedMinutesAgo + 10),
    queryFrom: minutesAgo(startedMinutesAgo + 10),
    queryTo: minutesAgo(startedMinutesAgo),
    cursorAfter: status === "Succeeded" ? minutesAgo(startedMinutesAgo) : null,
    createdAt: minutesAgo(startedMinutesAgo),
    updatedAt: minutesAgo(Math.max(0, startedMinutesAgo - 1)),
    startedAt: minutesAgo(startedMinutesAgo),
    completedAt: status === "Running" ? null : minutesAgo(Math.max(0, startedMinutesAgo - 1)),
    ...counts,
  };
}

const candidates = [
  candidate("c0000001-0000-4000-8000-000000000001", "INV190970", "DEB2062", "CENT", null, "Unmatched", "NeedsReview", false, 25),
  candidate("c0000001-0000-4000-8000-000000000002", "INV190968", "DEB2062", "FOUR", "1220104411", "Matched", "Ready", true, 40),
  candidate("c0000001-0000-4000-8000-000000000003", "INV190961", "DEB2062", "BRAC", "1220102998", "Matched", "Ready", true, 1500),
  candidate("c0000001-0000-4000-8000-000000000004", "INV190955", "DEB2062", "DURB", "1220102874", "Matched", "Submitted", false, 1600),
  candidate("c0000001-0000-4000-8000-000000000005", "INV190951", "DEB2062", "MIDR", "1220102761", "Ambiguous", "NeedsReview", false, 2900),
];

function candidate(id, invoiceNumber, customerAccount, location, po, match, status, canSubmit, updatedMinutesAgo) {
  return {
    id,
    invoiceNumber,
    customerAccount,
    customerLocation: location,
    shopritePurchaseOrderNumber: po ?? undefined,
    matchedShopritePurchaseOrderId: match === "Matched" ? `p-${po}` : undefined,
    purchaseOrderMatchStatus: match,
    storeDcGln: "6001007000207",
    status,
    canSubmit,
    updatedAt: minutesAgo(updatedMinutesAgo),
  };
}

function candidateDetail(summary) {
  const blocked = !summary.canSubmit && summary.status === "NeedsReview";
  return {
    id: summary.id,
    status: summary.status,
    canSubmit: summary.canSubmit,
    canonicalInvoice: {
      acumaticaInvoiceId: `AR-${summary.invoiceNumber}`,
      invoiceNumber: summary.invoiceNumber,
      customerAccount: summary.customerAccount,
      customerLocation: summary.customerLocation,
      shopritePurchaseOrderNumber: summary.shopritePurchaseOrderNumber,
      supplierGln: "6001197000006",
      sellerVatRegistrationNumber: "4010137059",
      storeDcGln: summary.storeDcGln,
      countryCode: "ZA",
      currencyCode: "ZAR",
      invoiceDate: daysAgo(1),
      totalExcludingTax: { currencyCode: "ZAR", amount: 16019.48 },
      totalIncludingTax: { currencyCode: "ZAR", amount: 18422.4 },
      totalTax: { currencyCode: "ZAR", amount: 2402.92 },
      lines: [
        { lineNumber: 1, acumaticaInventoryId: "ENER12", gtin: "6001007611212", description: "Energade lemon 500 ml", quantity: 12, acumaticaUom: "CASE", shopriteUom: "CS", isShopriteUomVerified: true, isCatchWeight: false },
        { lineNumber: 2, acumaticaInventoryId: "ENER10", gtin: "6001007611014", description: "Energade naartjie 500 ml", quantity: 10, acumaticaUom: "CASE", shopriteUom: "CS", isShopriteUomVerified: true, isCatchWeight: false },
        { lineNumber: 3, acumaticaInventoryId: "DYNA31", gtin: "6009880461317", description: "Octane orange 1 kg", quantity: 8, acumaticaUom: "EACH", isShopriteUomVerified: false, isCatchWeight: false },
      ],
    },
    matchedPurchaseOrder: summary.matchedShopritePurchaseOrderId
      ? {
          id: summary.matchedShopritePurchaseOrderId,
          purchaseOrderNumber: summary.shopritePurchaseOrderNumber,
          orderTypeCode: "220",
          orderTypeLabel: "Distribution centre order",
          deliveryGln: "6001007000207",
          deliveryLocationCode: "CENT",
          deliveryLocationName: "Shoprite Centurion DC",
          deliveryLocationSource: "PurchaseOrder",
          lineCount: 3,
          lines: [
            { id: "pl-1", lineNumber: 1, gtin: "6001007611212", buyerItemId: "100234", buyerItemDescription: "ENERGADE LEMON 500ML", requestedQuantity: 12 },
            { id: "pl-2", lineNumber: 2, gtin: "6001007611014", buyerItemId: "100235", buyerItemDescription: "ENERGADE NAARTJIE 500ML", requestedQuantity: 10 },
          ],
        }
      : undefined,
    validation: {
      canSubmit: summary.canSubmit,
      issues: blocked
        ? [
            { code: "MissingPurchaseOrderNumber", message: "The invoice has no Shoprite PO number.", severity: "Blocking", fixLocation: "Acumatica sales order: customer order number" },
            { code: "UnverifiedShopriteUom", message: "DYNA31 has no verified Shoprite unit of measure.", severity: "Warning", fixLocation: "Inventory mappings" },
          ]
        : [],
    },
    generatedXml: summary.canSubmit ? "<Invoice>\n  <InvoiceNumber>" + summary.invoiceNumber + "</InvoiceNumber>\n</Invoice>" : undefined,
    attempts:
      summary.status === "Submitted"
        ? [{ id: "att-1", initiatedBy: "developer@pvm.co.za", initiationMode: "Manual", status: "Accepted", responseStatusCode: 200, createdAt: daysAgo(1) }]
        : [],
  };
}

const purchaseOrders = [
  po("p-1220104411", "1220104411", "FOUR", "Checkers Hyper Fourways", 3, 50),
  po("p-1220102998", "1220102998", "BRAC", "Shoprite Brackenfell DC", 12, 1600),
  po("p-1220102874", "1220102874", "DURB", "Checkers Durbanville", 4, 1700),
];

function po(id, number, code, name, lineCount, seenMinutesAgo) {
  return {
    id,
    purchaseOrderNumber: number,
    orderTypeCode: "220",
    orderTypeLabel: "Distribution centre order",
    deliveryGln: "6001007000207",
    deliveryLocationCode: code,
    deliveryLocationName: name,
    deliveryLocationSource: "PurchaseOrder",
    supplierGln: "6001197000006",
    lineCount,
    lastSeenAt: minutesAgo(seenMinutesAgo),
  };
}

function poDetail(summary) {
  return {
    ...summary,
    orderHeaderId: `OH-${summary.purchaseOrderNumber}`,
    buyerGln: "6001007000009",
    currencyCode: "ZAR",
    totalExcludingTax: 12345.67,
    totalIncludingTax: 14197.52,
    totalTax: 1851.85,
    sourceEnvironment: "Production",
    sourceEndpoint: "/orders",
    payloadHash: "9f2c1b7a",
    shopriteCreatedAt: daysAgo(3),
    shopriteLastUpdatedAt: daysAgo(2),
    firstSeenAt: daysAgo(3),
    lines: [
      { id: "pl-1", lineNumber: 1, gtin: "6001007611212", buyerItemId: "100234", buyerItemDescription: "ENERGADE LEMON 500ML", supplierItemId: "ENER12", description: "Energade lemon 500 ml", requestedQuantity: 12, measurementUnitCode: "CS", netAmount: 6912, netPrice: 576, monetaryAmountExcludingTaxes: 6912, monetaryAmountIncludingTaxes: 7948.8 },
      { id: "pl-2", lineNumber: 2, gtin: "6001007611014", buyerItemId: "100235", buyerItemDescription: "ENERGADE NAARTJIE 500ML", supplierItemId: "ENER10", description: "Energade naartjie 500 ml", requestedQuantity: 10, measurementUnitCode: "CS", netAmount: 5760, netPrice: 576, monetaryAmountExcludingTaxes: 5760, monetaryAmountIncludingTaxes: 6624 },
    ],
    linkedInvoiceCandidates: candidates
      .filter((c) => c.shopritePurchaseOrderNumber === summary.purchaseOrderNumber)
      .map((c) => ({ id: c.id, invoiceNumber: c.invoiceNumber, customerAccount: c.customerAccount, status: c.status, updatedAt: c.updatedAt })),
    rawOrderJson: JSON.stringify({ orderNumber: summary.purchaseOrderNumber, lines: summary.lineCount }, null, 2),
  };
}

const freshness = { status: "Healthy", lastSuccessfulRefreshAt: minutesAgo(12), ageMinutes: 12, staleAfterMinutes: 60, allowsAutomaticProcessing: true };
const pushHealth = { status: "Healthy", configured: true, lastReceivedAt: minutesAgo(9), ageMinutes: 9, sourceOccurredAt: minutesAgo(9), lastEventLagSeconds: 4, eventCount: 412, duplicateCount: 3 };

const policy = (version, mode, createdMinutesAgo, reason) => ({
  version,
  mode,
  emergencyStop: false,
  accountAllowlist: ["DEB2062"],
  locationAllowlist: ["CENT", "BRAC"],
  supportedOrderTypes: ["220"],
  stabilizationDelayMinutes: 30,
  purchaseOrderFreshnessMinutes: 60,
  acumaticaFreshnessMinutes: 30,
  dailyAutomaticSubmissionCap: 25,
  automaticWindowStart: "07:00",
  automaticWindowEnd: "17:00",
  timeZoneId: "Africa/Johannesburg",
  createdBy: "developer@pvm.co.za",
  reason,
  createdAt: minutesAgo(createdMinutesAgo),
});

const exceptions = [
  exception("e-1", "AmbiguousSubmission", "High", "Open", "c0000001-0000-4000-8000-000000000005", "INV190951", "ShopriteTimeout", "Shoprite did not answer within 100 s. The submission may or may not have arrived.", "Check the Shoprite portal, then record the outcome", "ManualResolution", undefined, 3, 300, true),
  exception("e-2", "NeedsReview", "Medium", "Open", "c0000001-0000-4000-8000-000000000001", "INV190970", "MissingPurchaseOrderNumber", "INV190970 has no Shoprite PO number.", "Acumatica sales order: customer order number", "FixThenRevalidate", "developer@pvm.co.za", 1, 25, false),
  exception("e-3", "DeadLetter", "Medium", "Open", undefined, undefined, "MaxDeliveryCountExceeded", "Queue message dead-lettered after 5 attempts: acumatica-invoice-discovery.", "Replay the message, or resolve it with a reason", "safe-to-replay", undefined, 1, 2200, false),
];

function exception(id, category, severity, status, invoiceCandidateId, invoiceNumber, errorCode, summary, fixLocation, retryClassification, owner, occurrenceCount, firstSeenMinutesAgo, isOverdue) {
  return {
    id,
    deduplicationKey: `${category}:${invoiceNumber ?? id}`,
    category,
    severity,
    status,
    entityType: category === "SubmissionAmbiguous" || category === "AmbiguousSubmission" ? "SubmissionOperation" : invoiceCandidateId ? "InvoiceCandidate" : "MessageDelivery",
    entityId: invoiceCandidateId ?? "d-1",
    invoiceCandidateId,
    invoiceNumber,
    errorCode,
    summary,
    fixLocation,
    retryClassification,
    owner,
    occurrenceCount,
    latestEvidence: "HTTP 504 from the Shoprite gateway at 05:01",
    firstSeenAt: minutesAgo(firstSeenMinutesAgo),
    lastSeenAt: minutesAgo(Math.max(1, firstSeenMinutesAgo - 20)),
    dueAt: minutesAgo(firstSeenMinutesAgo - 240),
    isOverdue,
    comments: owner ? [{ id: "cm-1", actor: owner, body: "Asked Izelle to add the PO number on the sales order.", createdAt: minutesAgo(20) }] : [],
  };
}

const statements = (prefix, start, lines, closing) => [
  { referenceNbr: `${prefix}1318`, startBalanceDate: daysAgo(1), endBalanceDate: daysAgo(1), beginningBalance: closing - 4100, endingBalance: closing, lineCount: lines },
  { referenceNbr: `${prefix}1301`, startBalanceDate: daysAgo(3), endBalanceDate: daysAgo(2), beginningBalance: start, endingBalance: closing - 4100, lineCount: lines + 3 },
];

const routes = {
  "GET /api/auth/me": () => ({ id: "u-1", email: "developer@pvm.co.za", displayName: "Marius Bloemhof", roles: ["Admin"] }),
  "GET /api/operations/summary": () => ({
    environmentName: "Production",
    automationMode: "Shadow",
    automationEmergencyStop: false,
    automationPolicyVersion: 7,
    generatedAt: minutesAgo(0),
    purchaseOrderFreshness: freshness,
    acumaticaReconciliationFreshness: { status: "Healthy", lastSuccessfulReconciliationAt: minutesAgo(8), cursorAfter: minutesAgo(8), ageMinutes: 8, staleAfterMinutes: 30 },
    acumaticaPushNotificationHealth: pushHealth,
    summary: { activeRuns: 1, failedRuns: 1, pendingMessages: 2, deadLetters: 1, candidateInvoices: 52, needsReview: 2 },
    latestRuns: empty ? [] : runs,
  }),
  "GET /api/invoices/candidates": () => (empty ? [] : candidates),
  "GET /api/invoices/candidates/:id": (id) => candidateDetail(candidates.find((c) => c.id === id) ?? candidates[0]),
  "POST /api/invoices/refresh": () => ({ received: 3, created: 1, updated: 2 }),
  "POST /api/invoices/:id/submit": () => ({ status: "Accepted", message: "Shoprite accepted the invoice." }),
  "GET /api/shoprite/purchase-orders": () => (empty ? [] : purchaseOrders),
  "GET /api/shoprite/purchase-orders/freshness": () => freshness,
  "GET /api/shoprite/purchase-orders/:id": (id) => poDetail(purchaseOrders.find((p) => p.id === id) ?? purchaseOrders[0]),
  "POST /api/shoprite/purchase-orders/refresh": () => ({ runId: runs[4].id, messageId: "msg-0005", created: true }),
  "POST /api/shoprite/purchase-orders/:id/seed-test-invoice": () => candidates[1],
  "GET /api/integration-runs": () => (empty ? [] : runs),
  "GET /api/integration-runs/:id": (id) => runs.find((r) => r.id === id) ?? runs[0],
  "GET /api/automation/policy": () => ({
    environmentName: "Production",
    policy: policy(7, "Shadow", 3000, "Shadow mode for the first production week."),
    decisionSummary: { evaluated: 52, wouldSubmit: 51, queued: 0, excluded: 1, disabled: 0, emergencyStopped: 0 },
    recentDecisions: candidates.slice(0, 3).map((c, index) => ({
      id: `d-${index}`,
      invoiceCandidateId: c.id,
      invoiceNumber: c.invoiceNumber,
      policyVersion: 7,
      outcome: c.canSubmit ? "WouldSubmit" : "Excluded",
      reasonCodes: c.canSubmit ? ["ShadowMode"] : ["ValidationBlocked"],
      summary: c.canSubmit ? "Would submit. Shadow mode sends nothing." : "Excluded: the invoice has a blocking validation issue.",
      evaluatedAt: minutesAgo(10 + index * 7),
    })),
    recentVersions: [policy(7, "Shadow", 3000, "Shadow mode for the first production week."), policy(6, "Disabled", 9000, "Production estate created.")],
  }),
  "PUT /api/automation/policy": () => routes["GET /api/automation/policy"](),
  "POST /api/automation/emergency-stop": () => routes["GET /api/automation/policy"](),
  "GET /api/admin/acumatica-events": () => ({
    health: pushHealth,
    events: empty
      ? []
      : [0, 1, 2].map((index) => ({
          id: `ev-${index}`,
          sourceEnvironment: "Production",
          companyId: "PVM",
          queryName: "PVM-Shoprite-Finalized-Invoices",
          transactionId: `tx-${8812 + index}`,
          notificationTimestamp: now - index * 600_000,
          payloadHash: `a1b2c${index}`,
          insertedCount: 1,
          deletedCount: 0,
          enqueuedCount: index === 2 ? 0 : 1,
          duplicateCount: index === 2 ? 1 : 0,
          receivedAt: minutesAgo(9 + index * 10),
          lastReceivedAt: minutesAgo(9 + index * 10),
        })),
  }),
  "GET /api/admin/integration-messages": () => ({
    summary: { outboxTotal: 412, deliveryTotal: 409, pending: 2, published: 409, retrying: 0, deadLettered: 1 },
    outbox: empty
      ? []
      : [
          { id: "o-1", queueName: "acumatica-invoice-discovery", messageType: "DiscoverInvoices", correlationId: "corr-0001", status: "Published", publishAttempts: 1, createdAt: minutesAgo(9), updatedAt: minutesAgo(9), publishedAt: minutesAgo(9) },
          { id: "o-2", queueName: "shoprite-po-refresh", messageType: "RefreshPurchaseOrders", correlationId: "corr-0005", status: "Pending", publishAttempts: 0, createdAt: minutesAgo(2), updatedAt: minutesAgo(2) },
        ],
    deliveries: empty
      ? []
      : [
          { id: "d-1", queueName: "acumatica-invoice-discovery", messageId: "msg-0911", messageType: "DiscoverInvoices", correlationId: "corr-0911", status: "DeadLettered", deliveryCount: 5, errorCode: "AcumaticaTimeout", errorSummary: "Acumatica did not answer.", deadLetterReason: "MaxDeliveryCountExceeded", enqueuedAt: minutesAgo(2200), updatedAt: minutesAgo(2180) },
          { id: "d-2", queueName: "acumatica-invoice-discovery", messageId: "msg-0001", messageType: "DiscoverInvoices", correlationId: "corr-0001", status: "Completed", deliveryCount: 1, enqueuedAt: minutesAgo(9), updatedAt: minutesAgo(8), completedAt: minutesAgo(8) },
        ],
  }),
  "POST /api/admin/integration-messages/:command": () => ({ messageId: "msg-new" }),
  "GET /api/admin/inventory-mappings": () =>
    empty
      ? []
      : [
          {
            inventoryId: "ENER12",
            description: "Energade lemon 500 ml",
            acumaticaUom: "CASE",
            acumaticaGtins: ["6001007611212"],
            itemMappings: [{ id: "im-1", shopriteBuyerItemId: "100234", gtin: "6001007611212", isVerified: true, updatedBy: "developer@pvm.co.za", updatedAt: daysAgo(20) }],
            uomMapping: { id: "um-1", shopriteUom: "CS", isVerified: true, updatedBy: "developer@pvm.co.za", updatedAt: daysAgo(20) },
            suggestions: [],
            affectedCandidateCount: 4,
            unresolvedCandidateCount: 0,
          },
          {
            inventoryId: "DYNA31",
            description: "Octane orange 1 kg",
            acumaticaUom: "EACH",
            acumaticaGtins: ["6009880461317"],
            itemMappings: [],
            uomMapping: null,
            suggestions: [{ purchaseOrderLineId: "pl-9", purchaseOrderNumber: "1220102998", lineNumber: 4, shopriteBuyerItemId: "208811", gtin: "6009880461317", description: "OCTANE ORANGE 1KG" }],
            affectedCandidateCount: 1,
            unresolvedCandidateCount: 1,
          },
        ],
  "GET /api/admin/inventory-mappings/shoprite-items": () => [
    { shopriteBuyerItemId: "100234", description: "ENERGADE LEMON 500ML", gtins: ["6001007611212"], supplierItemIds: ["ENER12"], measurementUnitCodes: ["CS"], purchaseOrderCount: 14, latestPurchaseOrderNumber: "1220104411", representativePurchaseOrderLineId: "pl-1", mappedInventoryIds: ["ENER12"], isMapped: true },
    { shopriteBuyerItemId: "208811", description: "OCTANE ORANGE 1KG", gtins: ["6009880461317"], supplierItemIds: [], measurementUnitCodes: ["EA"], purchaseOrderCount: 2, latestPurchaseOrderNumber: "1220102998", representativePurchaseOrderLineId: "pl-9", mappedInventoryIds: [], isMapped: false },
  ],
  "GET /api/admin/inventory-mappings/acumatica-items/:id": (id) => ({ inventoryId: id, description: "Octane orange 1 kg", status: "Active", unitsOfMeasure: ["EACH", "CASE"] }),
  "PUT /api/admin/inventory-mappings/:id/:uom": () => ({ revalidatedCandidateCount: 1, message: "Mapping saved. 1 invoice was checked again." }),
  "GET /api/exceptions": () => ({
    tasks: empty ? [] : exceptions,
    summary: { ambiguous: 1, rejected: 0, needsReview: 1, deadLetters: 1, stuck: 0, held: 0, overdue: 1, resolved: 12 },
  }),
  "POST /api/exceptions/*": () => ({}),
  "GET /api/banking/status": () => ({
    accounts: [
      { bank: "Investec", cashAccount: "PVMINVEST", mode: "Feed", available: true, importedThrough: daysAgo(1), closingBalance: -12480.33, recentStatements: statements("00", -8000, 14, -12480.33) },
      { bank: "Nedbank", cashAccount: "PVMNEDBANK", mode: "Upload", available: true, importedThrough: daysAgo(1), closingBalance: 74116.2, recentStatements: statements("00", 70000, 8, 74116.2) },
    ],
  }),
  "POST /api/banking/import/nedbank/preview": () => ({
    cashAccount: "PVMNEDBANK",
    sourceAccountNumber: "••••4346",
    periodStart: daysAgo(3),
    periodEnd: daysAgo(1),
    transactionCount: 4,
    moneyIn: 8806.4,
    moneyOut: 1597.95,
    openingBalance: 31004.1,
    closingBalance: 38212.55,
    importedThrough: daysAgo(2),
    importedThroughBalance: 35241.48,
    balancesCarryForward: true,
    overlapsImportedPeriod: true,
    alreadyImportedCount: 1,
    newCount: 3,
    nothingToImport: false,
    uncoveredDates: [],
    lines: [
      { date: daysAgo(3), description: "ABSA BANK CAS1074 MidwaterSpar", receipt: 3919.43, disbursement: 0, alreadyImported: false },
      { date: daysAgo(3), description: "SAVEWAY SUPERSPAR", receipt: 1915.9, disbursement: 0, alreadyImported: false },
      { date: daysAgo(1), description: "SHELL MIDDELST", receipt: 0, disbursement: 1597.95, alreadyImported: false },
      { date: daysAgo(2), description: "TRF NA CPG", receipt: 2971.07, disbursement: 0, alreadyImported: true },
    ],
  }),
  "POST /api/banking/import/nedbank": () => ({ fileName: "Statement.ofx", linesImported: 3, statementReference: "001319", periodStart: daysAgo(3), periodEnd: daysAgo(1), openingBalance: 31004.1, closingBalance: 38212.55, alreadyImportedCount: 1 }),
  "GET /api/admin/users": () => [
    { id: "u-1", entraObjectId: "35425387-d19a-4e63-97b5-2165cce0032b", email: "developer@pvm.co.za", displayName: "Marius Bloemhof", status: "Active", roles: ["Admin"], createdAt: daysAgo(90), updatedAt: daysAgo(2), lastLoginAt: minutesAgo(30) },
    { id: "u-2", email: "izelle@example.test", displayName: "Izelle", status: "Active", roles: ["Operator"], createdAt: daysAgo(40), updatedAt: daysAgo(10), lastLoginAt: minutesAgo(180) },
    { id: "u-3", email: "viewer@example.test", displayName: "Read-only user", status: "Disabled", roles: ["Viewer"], createdAt: daysAgo(30), updatedAt: daysAgo(5) },
  ],
  "POST /api/admin/users": () => ({}),
  "PUT /api/admin/users/:id/roles": () => ({}),
  "PUT /api/admin/users/:id/status": () => ({}),
};

function match(method, pathname) {
  for (const [key, handler] of Object.entries(routes)) {
    const [routeMethod, routePath] = key.split(" ");
    if (routeMethod !== method) continue;
    if (routePath.endsWith("/*") && pathname.startsWith(routePath.slice(0, -1))) return { handler, params: [] };
    const routeParts = routePath.split("/");
    const parts = pathname.split("/");
    if (routeParts.length !== parts.length) continue;
    const params = [];
    if (routeParts.every((part, index) => (part.startsWith(":") ? (params.push(decodeURIComponent(parts[index])), true) : part === parts[index]))) {
      return { handler, params };
    }
  }
  return null;
}

createServer((request, response) => {
  const url = new URL(request.url ?? "/", "http://localhost");
  const found = match(request.method ?? "GET", url.pathname);
  // Drain any request body so the client does not wait.
  request.resume();
  if (!found) {
    response.writeHead(404, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ message: `No mock for ${request.method} ${url.pathname}` }));
    console.log(`404 ${request.method} ${url.pathname}`);
    return;
  }
  const body = found.handler(...found.params);
  response.writeHead(200, { "Content-Type": "application/json" });
  response.end(JSON.stringify(body));
  console.log(`200 ${request.method} ${url.pathname}`);
}).listen(port, "127.0.0.1", () => {
  console.log(`Mock backoffice API on http://127.0.0.1:${port}${empty ? " (empty lists)" : ""}`);
});

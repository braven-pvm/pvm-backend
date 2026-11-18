// A local stand-in for the backoffice API, for UI work only.
//
//   node scripts/mock-api.mjs --port 59999
//
// Point the workbench at it with NEXT_PUBLIC_API_BASE_URL=http://127.0.0.1:59999
// and AUTH_MODE=DevelopmentBypass. Every response is example data in the shape
// of src/api/client.ts. Writes answer with success and change nothing.
// set MOCK_EMPTY=1 in the environment of this script.
// For the "scanner setup is not configured" answer of the setup-code
// endpoints, set MOCK_SCANNER_SETUP=unconfigured.

import { randomBytes, randomUUID } from "node:crypto";
import { createServer } from "node:http";

const portIndex = process.argv.indexOf("--port");
const port = portIndex > 0 ? Number(process.argv[portIndex + 1]) : 5000;
const empty = process.env.MOCK_EMPTY === "1";
const scannerSetupConfigured = process.env.MOCK_SCANNER_SETUP !== "unconfigured";

const now = Date.now();
const minutesAgo = (minutes) => new Date(now - minutes * 60_000).toISOString();
const daysAgo = (days) => minutesAgo(days * 24 * 60);

const runs = [
  run("6f1d0c2a-0001-4a7e-9a10-000000000001", "acumatica-invoice-reconciliation", "scheduled", "Succeeded", 8, { receivedCount: 3, createdCount: 1, updatedCount: 2 }),
  run("6f1d0c2a-0002-4a7e-9a10-000000000002", "acumatica-invoice-reconciliation", "scheduled", "Failed", 18, { errorCode: "AcumaticaTimeout", errorSummary: "Acumatica did not answer within 100 s." }),
  run("6f1d0c2a-0003-4a7e-9a10-000000000003", "acumatica-invoice-reconciliation", "scheduled", "Succeeded", 28, { receivedCount: 12, unchangedCount: 12 }),
  run("6f1d0c2a-0004-4a7e-9a10-000000000004", "acumatica-invoice-reconciliation", "daily-lookback", "Succeeded", 520, { receivedCount: 41, unchangedCount: 41 }),
  run("6f1d0c2a-0005-4a7e-9a10-000000000005", "acumatica-invoice-reconciliation", "manual", "Running", 2, {}),
  run("6f1d0c2a-0006-4a7e-9a10-000000000006", "shoprite-po-refresh", "scheduled", "Succeeded", 50000, { receivedCount: 4, unchangedCount: 4 }),
];

function run(id, runType, trigger, status, startedMinutesAgo, counts) {
  return {
    id,
    runType,
    trigger,
    initiatedBy: trigger === "manual" ? "developer@pvm.co.za" : "scheduler",
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

// Captured documents (src/api/documents.ts). One of each case the pages show:
// filed (attached and waiting), needs review with candidates, unreadable,
// failed, stuck, sent back to review, and a rejected one that only the search finds.
const invoicePool = [
  { referenceNumber: "INV001234", date: daysAgo(16).slice(0, 10), customerId: "C0042", customerName: "Acme Stores (Pty) Ltd", parentCustomerName: null, shipToName: "Acme Middelburg", status: "Open" },
  { referenceNumber: "INV001235", date: daysAgo(16).slice(0, 10), customerId: "C0042", customerName: "Acme Stores (Pty) Ltd", parentCustomerName: null, shipToName: "Acme Witbank", status: "Open" },
  { referenceNumber: "INV001198", date: daysAgo(20).slice(0, 10), customerId: "C0107", customerName: "Saveway SuperSpar", parentCustomerName: "Spar Group", shipToName: null, status: "Closed" },
  { referenceNumber: "INV001240", date: daysAgo(3).slice(0, 10), customerId: "C0230", customerName: "Midwater Spar", parentCustomerName: "Spar Group", shipToName: null, status: "Open" },
  { referenceNumber: "INV001241", date: daysAgo(2).slice(0, 10), customerId: "C0311", customerName: "Shoprite Checkers DC Centurion", parentCustomerName: null, shipToName: null, status: "Open" },
];
const invoiceByNumber = (number) => invoicePool.find((invoice) => invoice.referenceNumber === number);

const documents = [
  documentRecord("d0c00000-0000-4000-8000-000000000001", "Filed", null, { uploaded: 2 * 24 * 60, matchedReference: "INV001234", matchedBy: "auto", attachedMinutesAgo: 2 * 24 * 60 - 3, extraction: { invoiceNumbers: ["INV001234"], salesOrderNumbers: ["SO004410"], customerOrderNumbers: ["PO77812"], clientName: "Acme Stores", deliveryDate: daysAgo(2).slice(0, 10), signaturePresent: true, note: null } }),
  documentRecord("d0c00000-0000-4000-8000-000000000002", "Filed", null, { uploaded: 26 * 60, matchedReference: "INV001198", matchedBy: "user:izelle@example.test", resolvedBy: "izelle@example.test", resolvedMinutesAgo: 25 * 60, attachPending: true, extraction: { InvoiceNumbers: ["INV001198", "INV001199"], ClientName: "Saveway", DeliveryDate: daysAgo(1).slice(0, 10), SignaturePresent: true } }),
  documentRecord("d0c00000-0000-4000-8000-000000000003", "NeedsReview", "MultipleCandidates", { uploaded: 6 * 60, device: "Dispatch phone 1", staff: "Thabo", extraction: { invoiceNumbers: ["INV00123"], salesOrderNumbers: [], customerOrderNumbers: [], clientName: "Acme", deliveryDate: daysAgo(0).slice(0, 10), signaturePresent: true, note: "Last digit of the invoice number is torn off" }, candidates: [invoiceByNumber("INV001234"), invoiceByNumber("INV001235")] }),
  documentRecord("d0c00000-0000-4000-8000-000000000004", "NeedsReview", "NoSecondSignal", { uploaded: 4 * 60, device: "Dispatch phone 1", extraction: { invoiceNumbers: ["INV001240"], salesOrderNumbers: [], customerOrderNumbers: [], clientName: null, deliveryDate: null, signaturePresent: false, note: null }, candidates: [invoiceByNumber("INV001240")] }),
  documentRecord("d0c00000-0000-4000-8000-000000000005", "NeedsReview", "ReadFailure", { uploaded: 3 * 60, unreadableReason: "The page is too blurred to read any number." }),
  documentRecord("d0c00000-0000-4000-8000-000000000006", "Failed", null, { uploaded: 2 * 60 }),
  documentRecord("d0c00000-0000-4000-8000-000000000007", "Processing", null, { uploaded: 95, stuck: true }),
  documentRecord("d0c00000-0000-4000-8000-000000000009", "NeedsReview", null, { uploaded: 5 * 60, resolvedBy: "izelle@example.test", resolvedMinutesAgo: 30, reviewNote: "Filed to the wrong invoice of a split delivery.", extraction: { invoiceNumbers: ["INV001235"], salesOrderNumbers: ["SO004410"], customerOrderNumbers: ["PO77812"], clientName: "Acme Stores", deliveryDate: daysAgo(0).slice(0, 10), signaturePresent: true, note: null }, candidates: [invoiceByNumber("INV001235"), invoiceByNumber("INV001234")] }),
  documentRecord("d0c00000-0000-4000-8000-000000000008", "NeedsReview", "NoCandidate", { uploaded: 3 * 24 * 60, rejectedBy: "izelle@example.test", rejectedMinutesAgo: 3 * 24 * 60 - 30, reviewNote: "Bad scan, the driver scanned it again.", extraction: { invoiceNumbers: [], clientName: "Unknown" } }),
];

function documentRecord(id, state, reviewReason, options) {
  const resolvedAt = options.resolvedMinutesAgo !== undefined ? minutesAgo(options.resolvedMinutesAgo) : options.rejectedMinutesAgo !== undefined ? minutesAgo(options.rejectedMinutesAgo) : null;
  return {
    id,
    clientDocumentId: id.replace("d0c0", "c11e"),
    documentType: "POD",
    state,
    reviewReason,
    stuck: options.stuck ?? false,
    pageCount: 1,
    sizeBytes: 182_311,
    matchedReference: options.matchedReference ?? null,
    matchedCustomerName: options.matchedReference ? invoiceByNumber(options.matchedReference)?.customerName ?? null : null,
    matchedBy: options.matchedBy ?? null,
    acumaticaAttachPending: options.attachPending ?? false,
    acumaticaAttachedAt: options.attachedMinutesAgo !== undefined ? minutesAgo(options.attachedMinutesAgo) : null,
    // A phone upload names the device; the staff name is optional. A workbench upload has neither.
    uploadedBy: options.device ? `device:${options.device}` : "driver.thabo@pvm.co.za",
    scannerDeviceName: options.device ?? null,
    scannerStaffName: options.staff ?? null,
    uploadedAt: minutesAgo(options.uploaded),
    resolvedBy: options.resolvedBy ?? options.rejectedBy ?? null,
    resolvedAt,
    reviewNote: options.reviewNote ?? null,
    rejectedBy: options.rejectedBy ?? null,
    rejectedAt: options.rejectedMinutesAgo !== undefined ? minutesAgo(options.rejectedMinutesAgo) : null,
    updatedAt: minutesAgo(Math.max(1, options.uploaded - 2)),
    extraction: options.extraction ?? null,
    unreadableReason: options.unreadableReason ?? null,
    candidates: options.candidates ?? [],
    acumaticaAttachMode: "Shadow",
  };
}

const searchFields = ["id", "documentType", "state", "reviewReason", "matchedReference", "matchedCustomerName", "matchedBy", "acumaticaAttachPending", "acumaticaAttachedAt", "pageCount", "uploadedBy", "scannerDeviceName", "scannerStaffName", "uploadedAt", "resolvedBy", "resolvedAt", "rejectedAt"];
const searchItem = (item) => Object.fromEntries(searchFields.map((field) => [field, item[field]]));

function searchDocuments(url) {
  const invoice = url.searchParams.get("invoice")?.toUpperCase();
  const client = url.searchParams.get("client")?.toLowerCase();
  const state = url.searchParams.get("state")?.toLowerCase();
  const type = url.searchParams.get("type")?.toUpperCase();
  const from = url.searchParams.get("from");
  const to = url.searchParams.get("to");
  const items = (empty ? [] : documents)
    .filter((item) => !invoice || item.matchedReference?.toUpperCase() === invoice)
    .filter((item) => !client || item.matchedCustomerName?.toLowerCase().includes(client))
    .filter((item) => !state || item.state.toLowerCase() === state)
    .filter((item) => !type || item.documentType === type)
    .filter((item) => !from || Date.parse(item.uploadedAt) >= Date.parse(from))
    .filter((item) => !to || Date.parse(item.uploadedAt) < Date.parse(to))
    .sort((a, b) => Date.parse(b.uploadedAt) - Date.parse(a.uploadedAt))
    .map(searchItem);
  return { items, page: 1, pageSize: 50, total: items.length };
}

function reviewList() {
  const items = (empty ? [] : documents)
    .filter((item) => item.stuck || ((item.state === "NeedsReview" || item.state === "Failed") && !item.rejectedAt))
    .sort((a, b) => Date.parse(a.uploadedAt) - Date.parse(b.uploadedAt))
    .map(({ id, documentType, state, reviewReason, stuck, pageCount, uploadedBy, scannerDeviceName, scannerStaffName, uploadedAt, updatedAt }) => ({ id, documentType, state, reviewReason, stuck, pageCount, uploadedBy, scannerDeviceName, scannerStaffName, uploadedAt, updatedAt }));
  return { items, page: 1, pageSize: 50, total: items.length };
}

// A one-page PDF that names the document, so the viewer has something to show.
function documentPdf(id) {
  const document = documents.find((item) => item.id === id);
  const lines = [
    "PROOF OF DELIVERY",
    `Invoice: ${document?.extraction?.invoiceNumbers?.join(", ") || document?.matchedReference || "unreadable"}`,
    `Client: ${document?.extraction?.clientName ?? "-"}`,
    "Received in good order. Signed: ________",
    `Mock document ${id.slice(-4)}`,
  ];
  const text = lines.map((line, index) => `BT /F1 ${index === 0 ? 20 : 12} Tf 60 ${760 - index * 32} Td (${line.replace(/[()\\]/g, "")}) Tj ET`).join("\n");
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
    `<< /Length ${text.length} >>\nstream\n${text}\nendstream`,
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
  ];
  let pdf = "%PDF-1.4\n";
  const offsets = objects.map((object, index) => {
    const offset = pdf.length;
    pdf += `${index + 1} 0 obj\n${object}\nendobj\n`;
    return offset;
  });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((offset) => `${String(offset).padStart(10, "0")} 00000 n \n`).join("")}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, "latin1");
}

// Problem responses, like the API's RFC 7807 answers.
const problem = (status, detail) => ({ __status: status, __body: { status, detail } });

// Scanner devices and staff names (src/api/scanner-devices.ts). One device in
// each state, and a setup-code answer with a realistic qrPayload.
const scannerApiBaseUrl = "https://ca-pvm-api-qa.blackbay-85d5b3d6.southafricanorth.azurecontainerapps.io";
const scannerDevices = [
  { id: "5ca11e70-0000-4000-8000-000000000001", name: "Dispatch phone 1", state: "Active", createdAt: daysAgo(12), enrolledAt: daysAgo(12), lastSeenAt: minutesAgo(4), revokedAt: null },
  { id: "5ca11e70-0000-4000-8000-000000000002", name: "Driver phone Bongani", state: "Active", createdAt: daysAgo(9), enrolledAt: daysAgo(9), lastSeenAt: daysAgo(2), revokedAt: null },
  { id: "5ca11e70-0000-4000-8000-000000000003", name: "Dispatch phone 2", state: "Pending", createdAt: minutesAgo(20), enrolledAt: null, lastSeenAt: null, revokedAt: null },
  { id: "5ca11e70-0000-4000-8000-000000000004", name: "Old Samsung", state: "Revoked", createdAt: daysAgo(60), enrolledAt: daysAgo(60), lastSeenAt: daysAgo(15), revokedAt: daysAgo(14) },
];
const scannerStaff = [
  { id: "57aff000-0000-4000-8000-000000000001", name: "Anna", active: true, createdAt: daysAgo(12), updatedAt: daysAgo(12) },
  { id: "57aff000-0000-4000-8000-000000000002", name: "Thabo", active: true, createdAt: daysAgo(12), updatedAt: daysAgo(3) },
  { id: "57aff000-0000-4000-8000-000000000003", name: "Sipho", active: false, createdAt: daysAgo(30), updatedAt: daysAgo(7) },
];
const scannerNotConfigured = () => problem(500, "Scanner setup is not configured: set DocumentCapture:DeviceApiBaseUrl");
const validScannerName = (value) => typeof value === "string" && value.trim().length > 0 && value.trim().length <= 100;

// A new setup code: 32 random bytes in base64url (43 characters), valid for 15 minutes.
function issuedSetupCode(device) {
  const setupCode = randomBytes(32).toString("base64url");
  return {
    device,
    setupCode,
    expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    qrPayload: JSON.stringify({ v: 1, api: scannerApiBaseUrl, code: setupCode }),
  };
}
const isOpen = (item) => !item.stuck && (item.state === "NeedsReview" || item.state === "Failed");

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
  "POST /api/shoprite/purchase-orders/refresh": () => ({ runId: runs[5].id, messageId: "msg-0005", created: true }),
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
  // Documents. The review routes come before "/:id" so they match first.
  "GET /api/documents": (context) => searchDocuments(context.url),
  "GET /api/documents/types": () => [{ code: "POD", displayName: "Proof of delivery" }],
  "GET /api/documents/review": () => reviewList(),
  "GET /api/documents/review/invoice-search": (context) => {
    const q = context.url.searchParams.get("q")?.trim().toLowerCase();
    if (!q) return problem(400, "The query 'q' is required.");
    return invoicePool.filter((invoice) => invoice.referenceNumber.toLowerCase().startsWith(q) || invoice.customerName.toLowerCase().includes(q));
  },
  "GET /api/documents/:id": (id) => documents.find((item) => item.id === id) ?? problem(404, "Not found."),
  "GET /api/documents/:id/file": (id) => (documents.some((item) => item.id === id) ? { __raw: documentPdf(id), contentType: "application/pdf", disposition: `inline; filename="POD-${id}.pdf"` } : problem(404, "Not found.")),
  // The Failed document answers 409, to show what happens when someone else resolved it first.
  "POST /api/documents/:id/file-to": (id, context) => {
    const item = documents.find((document) => document.id === id);
    if (!item) return problem(404, "Not found.");
    if (!isOpen(item) || item.state === "Failed") return problem(409, "The document is not on the review list.");
    const reference = String(context.json?.invoiceReference ?? "").trim();
    const invoice = invoicePool.find((candidate) => candidate.referenceNumber.toLowerCase() === reference.toLowerCase());
    if (!invoice) return problem(400, `Acumatica has no invoice with the reference '${reference}'.`);
    return { documentId: id, state: "Filed", matchedReference: invoice.referenceNumber, matchedCustomerName: invoice.customerName };
  },
  // Filed and not attached (the second filed document) can go back; the attached one answers 409.
  "POST /api/documents/:id/return-to-review": (id, context) => {
    const item = documents.find((document) => document.id === id);
    if (!item) return problem(404, "Not found.");
    const note = String(context.json?.note ?? "").trim();
    if (!note || note.length > 1000) return problem(400, "'note' is required, at most 1000 characters.");
    if (item.state !== "Filed") return problem(409, "The document is not filed; only a filed document can be sent back to review.");
    if (item.acumaticaAttachedAt) return problem(409, "The PDF is already attached to the invoice in Acumatica. Remove the attachment in Acumatica by hand; the document cannot be sent back to review from here.");
    return { documentId: id, state: "NeedsReview" };
  },
  // Failed and stuck documents can be processed again.
  "POST /api/documents/:id/process-again": (id) => {
    const item = documents.find((document) => document.id === id);
    if (!item) return problem(404, "Not found.");
    if (item.state !== "Failed" && !item.stuck) return problem(409, "Only a Failed document, or one stuck in Received or Processing for more than 30 minutes, can be processed again.");
    return { documentId: id, state: "Received" };
  },
  "POST /api/documents/:id/reject": (id, context) => {
    const item = documents.find((document) => document.id === id);
    if (!item) return problem(404, "Not found.");
    const note = String(context.json?.note ?? "").trim();
    if (!note || note.length > 1000) return problem(400, "'note' is required, at most 1000 characters.");
    if (!isOpen(item) || item.rejectedAt) return problem(409, "The document is not open on the review list.");
    return { documentId: id, rejected: true };
  },
  "GET /api/scanner-devices": () => (empty ? [] : scannerDevices),
  "POST /api/scanner-devices": (context) => {
    if (!scannerSetupConfigured) return scannerNotConfigured();
    const name = context.json?.name;
    if (!validScannerName(name)) return problem(400, "'name' is required, at most 100 characters.");
    const trimmed = name.trim();
    if (scannerDevices.some((device) => device.state !== "Revoked" && device.name.toLowerCase() === trimmed.toLowerCase())) {
      return problem(409, `A device named '${trimmed}' already exists.`);
    }
    const device = { id: randomUUID(), name: trimmed, state: "Pending", createdAt: new Date().toISOString(), enrolledAt: null, lastSeenAt: null, revokedAt: null };
    return { __status: 201, __body: issuedSetupCode(device) };
  },
  "POST /api/scanner-devices/:id/setup-code": (id) => {
    if (!scannerSetupConfigured) return scannerNotConfigured();
    const device = scannerDevices.find((item) => item.id === id);
    if (!device) return problem(404, "The scanner device was not found.");
    if (device.state === "Revoked") return problem(409, "The device is revoked; add it again as a new device.");
    return issuedSetupCode(device);
  },
  "POST /api/scanner-devices/:id/revoke": (id) => {
    const device = scannerDevices.find((item) => item.id === id);
    if (!device) return problem(404, "The scanner device was not found.");
    return { ...device, state: "Revoked", revokedAt: device.revokedAt ?? new Date().toISOString() };
  },
  "GET /api/scanner-staff": () => (empty ? [] : scannerStaff),
  "POST /api/scanner-staff": (context) => {
    if (!validScannerName(context.json?.name)) return problem(400, "'name' is required, at most 100 characters.");
    const now = new Date().toISOString();
    return { __status: 201, __body: { id: randomUUID(), name: context.json.name.trim(), active: true, createdAt: now, updatedAt: now } };
  },
  "PUT /api/scanner-staff/:id": (id, context) => {
    const staff = scannerStaff.find((item) => item.id === id);
    if (!validScannerName(context.json?.name) || typeof context.json?.active !== "boolean") {
      return problem(400, "'name' (at most 100 characters) and 'active' are required.");
    }
    if (!staff) return problem(404, "The staff name was not found.");
    return { ...staff, name: context.json.name.trim(), active: context.json.active, updatedAt: new Date().toISOString() };
  },
  "GET /api/stock/sync/latest": () => ({
    id: "run-1",
    mode: "Shadow",
    status: "Succeeded",
    startedAt: minutesAgo(14),
    completedAt: minutesAgo(12),
    itemCount: 6,
    updateCount: 2,
    unchangedCount: 2,
    skippedCount: 2,
    writtenCount: 0,
    detail: null,
    lines: [
      { sku: "CER07", productId: 812, variationId: null, action: "Update", currentQuantity: 3297, newQuantity: 0, acumaticaLevel: -15, webPending: 0, reason: null, figures: { onHand: 3307, heldBackAdjustments: 3000, autoAdjustments: 307, openOrders: 10, promoOrders: 0, transfersOut: 5, warehouseCommitted: null } },
      { sku: "DYNA39", productId: 901, variationId: null, action: "Update", currentQuantity: 0, newQuantity: 6, acumaticaLevel: 6, webPending: 0, reason: null },
      { sku: "DYNA22", productId: 640, variationId: null, action: "Unchanged", currentQuantity: 16, newQuantity: 16, acumaticaLevel: 16.5, webPending: 1, reason: null },
      { sku: "PROT20", productId: 512, variationId: null, action: "Unchanged", currentQuantity: 0, newQuantity: 0, acumaticaLevel: 3, webPending: 0, reason: null },
      { sku: "ENER1", productId: 300, variationId: 301, action: "Skip", currentQuantity: null, newQuantity: null, acumaticaLevel: null, webPending: 0, reason: "this variation takes its stock from the parent product" },
      { sku: "BOTT8", productId: 410, variationId: null, action: "Skip", currentQuantity: null, newQuantity: null, acumaticaLevel: null, webPending: 0, reason: "the web store does not track stock for this item" },
    ],
  }),
  "GET /api/banking/status": () => ({
    accounts: [
      { bank: "Investec", cashAccount: "PVMINVEST", mode: "Automatic", available: true, importedThrough: daysAgo(1), closingBalance: -12480.33, recentStatements: statements("00", -8000, 14, -12480.33) },
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
  const chunks = [];
  request.on("data", (chunk) => chunks.push(chunk));
  request.on("end", () => {
    if (!found) {
      response.writeHead(404, { "Content-Type": "application/json" });
      response.end(JSON.stringify({ message: `No mock for ${request.method} ${url.pathname}` }));
      console.log(`404 ${request.method} ${url.pathname}`);
      return;
    }
    let json;
    try {
      json = chunks.length > 0 ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : undefined;
    } catch {
      json = undefined;
    }
    // Handlers get the path parameters, then { url, json } for the ones that read the query or the body.
    const body = found.handler(...found.params, { url, json });
    if (body && body.__raw) {
      response.writeHead(200, { "Content-Type": body.contentType, "Content-Disposition": body.disposition });
      response.end(body.__raw);
      console.log(`200 ${request.method} ${url.pathname}`);
      return;
    }
    const status = body && body.__status ? body.__status : 200;
    response.writeHead(status, { "Content-Type": status < 400 ? "application/json" : "application/problem+json" });
    response.end(JSON.stringify(body && body.__status ? body.__body : body));
    console.log(`${status} ${request.method} ${url.pathname}`);
  });
}).listen(port, "127.0.0.1", () => {
  console.log(`Mock backoffice API on http://127.0.0.1:${port}${empty ? " (empty lists)" : ""}${scannerSetupConfigured ? "" : " (scanner setup not configured)"}`);
});

import { getApiAuthHeaders } from "../auth/session";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

type Nullable<T> = T | null;

export type InvoiceCandidateSummary = {
  id: string;
  invoiceNumber: string;
  customerAccount: string;
  customerLocation?: string;
  shopritePurchaseOrderNumber?: string;
  matchedShopritePurchaseOrderId?: string;
  purchaseOrderMatchStatus: string;
  storeDcGln?: string;
  status: string;
  canSubmit: boolean;
  updatedAt: string;
};

export type ValidationIssue = {
  code: string;
  message: string;
  severity: "Warning" | "Blocking";
  fixLocation: string;
};

export type InvoiceSubmissionAttempt = {
  id: string;
  initiatedBy: string;
  initiationMode: string;
  status: string;
  responseStatusCode?: number;
  errorMessage?: string;
  failureClassification?: string;
  isRetryEligible?: boolean;
  createdAt: string;
};

export type InvoiceCandidateDetail = {
  id: string;
  status: string;
  canSubmit: boolean;
  acumaticaInvoice?: unknown;
  canonicalInvoice?: {
    acumaticaInvoiceId: string;
    invoiceNumber: string;
    customerAccount: string;
    customerLocation?: string;
    shopritePurchaseOrderNumber?: string;
    supplierGln?: string;
    sellerVatRegistrationNumber?: string;
    storeDcGln?: string;
    countryCode: string;
    currencyCode: string;
    invoiceDate: string;
    totalExcludingTax: { currencyCode: string; amount: number };
    totalIncludingTax: { currencyCode: string; amount: number };
    totalTax: { currencyCode: string; amount: number };
    lines: Array<{
      lineNumber: number;
      acumaticaInventoryId: string;
      gtin?: string;
      description: string;
      quantity: number;
      acumaticaUom: string;
      shopriteUom?: string;
      isShopriteUomVerified: boolean;
      isCatchWeight: boolean;
    }>;
  };
  matchedPurchaseOrder?: {
    id: string;
    purchaseOrderNumber: string;
    orderTypeCode?: string;
    orderTypeLabel?: string;
    deliveryGln?: string;
    deliveryLocationCode?: string;
    deliveryLocationName?: string;
    deliveryLocationSource: string;
    lineCount: number;
    lines: Array<{
      id: string;
      lineNumber: number;
      gtin?: string;
      buyerItemId?: string;
      buyerItemDescription?: string;
      description?: string;
      requestedQuantity?: number;
    }>;
  };
  validation: {
    issues: ValidationIssue[];
    canSubmit: boolean;
  };
  generatedXml?: string;
  attempts: InvoiceSubmissionAttempt[];
};

export type SubmitInvoiceResult = {
  status: string;
  message: string;
};

export type PurchaseOrderSummary = {
  id: string;
  purchaseOrderNumber: string;
  orderTypeCode?: Nullable<string>;
  orderTypeLabel?: Nullable<string>;
  deliveryGln?: Nullable<string>;
  deliveryLocationCode?: Nullable<string>;
  deliveryLocationName?: Nullable<string>;
  deliveryLocationSource: string;
  supplierGln?: Nullable<string>;
  lineCount: number;
  lastSeenAt: string;
};

export type PurchaseOrderDetail = PurchaseOrderSummary & {
  orderHeaderId?: Nullable<string>;
  buyerGln?: Nullable<string>;
  currencyCode?: Nullable<string>;
  totalExcludingTax?: Nullable<number>;
  totalIncludingTax?: Nullable<number>;
  totalTax?: Nullable<number>;
  sourceEnvironment: string;
  sourceEndpoint: string;
  payloadHash?: Nullable<string>;
  shopriteCreatedAt?: Nullable<string>;
  shopriteLastUpdatedAt?: Nullable<string>;
  firstSeenAt: string;
  lines: Array<{
    id: string;
    lineNumber: number;
    gtin?: Nullable<string>;
    buyerItemId?: Nullable<string>;
    buyerItemDescription?: Nullable<string>;
    supplierItemId?: Nullable<string>;
    description?: Nullable<string>;
    requestedQuantity?: Nullable<number>;
    measurementUnitCode?: Nullable<string>;
    netAmount?: Nullable<number>;
    netPrice?: Nullable<number>;
    monetaryAmountExcludingTaxes?: Nullable<number>;
    monetaryAmountIncludingTaxes?: Nullable<number>;
  }>;
  linkedInvoiceCandidates: Array<{
    id: string;
    invoiceNumber: string;
    customerAccount: string;
    status: string;
    updatedAt: string;
  }>;
  rawOrderJson?: Nullable<string>;
};

export type QueuedIntegrationRun = {
  runId: string;
  messageId: string;
  created: boolean;
  statusUrl?: string;
};

export type ShopritePurchaseOrderFreshness = {
  status: "Healthy" | "Stale" | "Unknown";
  lastSuccessfulRefreshAt?: Nullable<string>;
  ageMinutes?: Nullable<number>;
  staleAfterMinutes: number;
  allowsAutomaticProcessing: boolean;
};

export type IntegrationRun = {
  id: string;
  runType: string;
  trigger: string;
  initiatedBy: string;
  environmentName: string;
  correlationId: string;
  messageId?: Nullable<string>;
  status: string;
  attemptCount: number;
  receivedCount: number;
  createdCount: number;
  updatedCount: number;
  unchangedCount: number;
  skippedCount: number;
  revalidatedCount: number;
  failedCount: number;
  errorCode?: Nullable<string>;
  errorSummary?: Nullable<string>;
  cursorBefore?: Nullable<string>;
  queryFrom?: Nullable<string>;
  queryTo?: Nullable<string>;
  cursorAfter?: Nullable<string>;
  createdAt: string;
  updatedAt: string;
  startedAt?: Nullable<string>;
  completedAt?: Nullable<string>;
};

export type OperationsSummary = {
  environmentName: string;
  automationMode: string;
  automationEmergencyStop: boolean;
  automationPolicyVersion: number;
  generatedAt: string;
  purchaseOrderFreshness: ShopritePurchaseOrderFreshness;
  acumaticaReconciliationFreshness: {
    status: "Healthy" | "Stale" | "Unknown";
    lastSuccessfulReconciliationAt?: Nullable<string>;
    cursorAfter?: Nullable<string>;
    ageMinutes?: Nullable<number>;
    staleAfterMinutes: number;
  };
  acumaticaPushNotificationHealth: AcumaticaPushNotificationHealth;
  summary: {
    activeRuns: number;
    failedRuns: number;
    pendingMessages: number;
    deadLetters: number;
    candidateInvoices: number;
    needsReview: number;
  };
  latestRuns: IntegrationRun[];
};

export type AcumaticaPushNotificationHealth = {
  status: "NotConfigured" | "Waiting" | "Healthy";
  configured: boolean;
  lastReceivedAt?: Nullable<string>;
  ageMinutes?: Nullable<number>;
  sourceOccurredAt?: Nullable<string>;
  lastEventLagSeconds?: Nullable<number>;
  eventCount: number;
  duplicateCount: number;
};

export type AcumaticaPushNotificationEventView = {
  health: AcumaticaPushNotificationHealth;
  events: Array<{
    id: string;
    sourceEnvironment: string;
    companyId: string;
    queryName: string;
    transactionId: string;
    notificationTimestamp: number;
    payloadHash: string;
    insertedCount: number;
    deletedCount: number;
    enqueuedCount: number;
    duplicateCount: number;
    receivedAt: string;
    lastReceivedAt: string;
  }>;
};

export type IntegrationMessageView = {
  summary: {
    outboxTotal: number;
    deliveryTotal: number;
    pending: number;
    published: number;
    retrying: number;
    deadLettered: number;
  };
  outbox: Array<{
    id: string;
    queueName: string;
    messageType: string;
    correlationId: string;
    status: string;
    publishAttempts: number;
    lastErrorCode?: Nullable<string>;
    lastErrorSummary?: Nullable<string>;
    createdAt: string;
    updatedAt: string;
    publishedAt?: Nullable<string>;
  }>;
  deliveries: Array<{
    id: string;
    queueName: string;
    messageId: string;
    messageType: string;
    correlationId: string;
    status: string;
    deliveryCount: number;
    errorCode?: Nullable<string>;
    errorSummary?: Nullable<string>;
    deadLetterReason?: Nullable<string>;
    enqueuedAt: string;
    updatedAt: string;
    completedAt?: Nullable<string>;
  }>;
};

export type InventoryMappingView = {
  inventoryId: string;
  description?: Nullable<string>;
  acumaticaUom: string;
  acumaticaGtins: string[];
  itemMappings: Array<{
    id: string;
    shopriteBuyerItemId: string;
    gtin: string;
    isVerified: boolean;
    updatedBy: string;
    updatedAt: string;
  }>;
  uomMapping?: Nullable<{
    id: string;
    shopriteUom: "EA" | "CA" | "CS" | "KG";
    isVerified: boolean;
    updatedBy: string;
    updatedAt: string;
  }>;
  suggestions: Array<{
    purchaseOrderLineId: string;
    purchaseOrderNumber: string;
    lineNumber: number;
    shopriteBuyerItemId?: Nullable<string>;
    gtin?: Nullable<string>;
    description?: Nullable<string>;
  }>;
  affectedCandidateCount: number;
  unresolvedCandidateCount: number;
};

export type ShopriteCatalogItem = {
  shopriteBuyerItemId: string;
  description?: Nullable<string>;
  gtins: string[];
  supplierItemIds: string[];
  measurementUnitCodes: string[];
  purchaseOrderCount: number;
  latestPurchaseOrderNumber: string;
  representativePurchaseOrderLineId: string;
  mappedInventoryIds: string[];
  isMapped: boolean;
};

export type AcumaticaInventoryItem = {
  inventoryId: string;
  description: string;
  status?: Nullable<string>;
  unitsOfMeasure: string[];
};

export type AutomationMode = "Disabled" | "Shadow" | "Allowlisted" | "Enabled";

export type AutomationPolicy = {
  version: number;
  mode: AutomationMode;
  emergencyStop: boolean;
  accountAllowlist: string[];
  locationAllowlist: string[];
  supportedOrderTypes: string[];
  stabilizationDelayMinutes: number;
  purchaseOrderFreshnessMinutes: number;
  acumaticaFreshnessMinutes: number;
  dailyAutomaticSubmissionCap: number;
  automaticWindowStart: string;
  automaticWindowEnd: string;
  timeZoneId: string;
  createdBy: string;
  reason: string;
  createdAt: string;
};

export type AutomationPolicyView = {
  environmentName: string;
  policy: AutomationPolicy;
  decisionSummary: {
    evaluated: number;
    wouldSubmit: number;
    queued: number;
    excluded: number;
    disabled: number;
    emergencyStopped: number;
  };
  recentDecisions: Array<{
    id: string;
    invoiceCandidateId: string;
    invoiceNumber: string;
    policyVersion: number;
    outcome: string;
    reasonCodes: string[];
    summary: string;
    notBefore?: Nullable<string>;
    commandId?: Nullable<string>;
    messageId?: Nullable<string>;
    evaluatedAt: string;
  }>;
  recentVersions: AutomationPolicy[];
};

export type AutomationPolicyChange = Omit<
  AutomationPolicy,
  "version" | "emergencyStop" | "createdBy" | "createdAt"
> & {
  expectedVersion: number;
  acknowledgeAutomaticSubmissions: boolean;
  environmentConfirmation?: string;
  typedConfirmation?: string;
};

export async function getInvoiceCandidates(): Promise<
  InvoiceCandidateSummary[]
> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/invoices/candidates`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load invoice candidates: ${response.status}`);
  }

  const data: unknown = await response.json();

  return Array.isArray(data) ? (data as InvoiceCandidateSummary[]) : [];
}

export async function getInvoiceCandidate(
  id: string,
): Promise<InvoiceCandidateDetail> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/invoices/candidates/${id}`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load invoice candidate: ${response.status}`);
  }

  return response.json();
}

export async function refreshInvoiceCandidates(): Promise<{
  received: number;
  created: number;
  updated: number;
}> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/invoices/refresh`, {
    method: "POST",
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to refresh invoice candidates: ${response.status}`);
  }

  return response.json();
}

export async function submitInvoice(id: string): Promise<SubmitInvoiceResult> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/invoices/${id}/submit`, {
    method: "POST",
    headers,
    cache: "no-store",
  });
  const result = await readJson(response);

  if (isSubmitInvoiceResult(result)) {
    return result;
  }

  if (!response.ok) {
    throw new Error(`Failed to submit invoice: ${response.status}`);
  }

  throw new Error("Failed to submit invoice: invalid API response.");
}

export async function getInventoryMappings(
  search?: string,
): Promise<InventoryMappingView[]> {
  const headers = await getApiAuthHeaders();
  const query = search ? `?search=${encodeURIComponent(search)}` : "";
  const response = await fetch(
    `${apiBaseUrl}/api/admin/inventory-mappings${query}`,
    { headers, cache: "no-store" },
  );

  if (!response.ok) {
    throw new Error(`Failed to load inventory mappings: ${response.status}`);
  }

  const data: unknown = await response.json();
  return Array.isArray(data) ? (data as InventoryMappingView[]) : [];
}

export async function getShopriteCatalogItems(
  search?: string,
): Promise<ShopriteCatalogItem[]> {
  const headers = await getApiAuthHeaders();
  const query = search ? `?search=${encodeURIComponent(search)}` : "";
  const response = await fetch(
    `${apiBaseUrl}/api/admin/inventory-mappings/shoprite-items${query}`,
    { headers, cache: "no-store" },
  );

  if (!response.ok) {
    throw new Error(`Failed to load Shoprite item catalogue: ${response.status}`);
  }

  const data: unknown = await response.json();
  return Array.isArray(data) ? (data as ShopriteCatalogItem[]) : [];
}

export async function getAcumaticaInventoryItem(
  inventoryId: string,
): Promise<AcumaticaInventoryItem | null> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/admin/inventory-mappings/acumatica-items/${encodeURIComponent(inventoryId)}`,
    { headers, cache: "no-store" },
  );

  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`Failed to load Acumatica inventory item: ${response.status}`);
  }

  return response.json();
}

export async function saveInventoryMapping(
  inventoryId: string,
  acumaticaUom: string,
  input: {
    purchaseOrderLineId: string;
    shopriteUom: "EA" | "CA" | "CS" | "KG";
    reason: string;
  },
): Promise<{ revalidatedCandidateCount: number; message: string }> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/admin/inventory-mappings/${encodeURIComponent(inventoryId)}/${encodeURIComponent(acumaticaUom)}`,
    {
      method: "PUT",
      headers: {
        ...headers,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(input),
      cache: "no-store",
    },
  );

  if (!response.ok) {
    const result = await readJson(response);
    const message = getErrorMessage(result);
    throw new Error(message ?? `Failed to save inventory mapping: ${response.status}`);
  }

  return response.json();
}

export async function getAutomationPolicy(): Promise<AutomationPolicyView> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/automation/policy`, {
    headers,
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`Failed to load automation policy: ${response.status}`);
  }
  return response.json();
}

export async function changeAutomationPolicy(
  input: AutomationPolicyChange,
): Promise<AutomationPolicyView> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/automation/policy`, {
    method: "PUT",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(input),
    cache: "no-store",
  });
  if (!response.ok) {
    const result = await readJson(response);
    throw new Error(getErrorMessage(result) ?? `Failed to change automation policy: ${response.status}`);
  }
  return response.json();
}

export async function setAutomationEmergencyStop(input: {
  expectedVersion: number;
  active: boolean;
  reason: string;
}): Promise<AutomationPolicyView> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/automation/emergency-stop`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(input),
    cache: "no-store",
  });
  if (!response.ok) {
    const result = await readJson(response);
    throw new Error(getErrorMessage(result) ?? `Failed to change emergency stop: ${response.status}`);
  }
  return response.json();
}

function getErrorMessage(value: unknown) {
  if (!value || typeof value !== "object") {
    return undefined;
  }

  const result = value as { message?: unknown };
  return typeof result.message === "string" ? result.message : undefined;
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json();
  } catch {
    return undefined;
  }
}

function isSubmitInvoiceResult(value: unknown): value is SubmitInvoiceResult {
  if (!value || typeof value !== "object") {
    return false;
  }

  const result = value as Partial<SubmitInvoiceResult>;
  return typeof result.status === "string" && typeof result.message === "string";
}

export async function getPurchaseOrders(): Promise<PurchaseOrderSummary[]> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/shoprite/purchase-orders`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load Shoprite purchase orders: ${response.status}`);
  }

  const data: unknown = await response.json();

  return Array.isArray(data) ? (data as PurchaseOrderSummary[]) : [];
}

export async function getPurchaseOrder(id: string): Promise<PurchaseOrderDetail> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/shoprite/purchase-orders/${id}`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load Shoprite purchase order: ${response.status}`);
  }

  return response.json();
}

export async function refreshPurchaseOrders(): Promise<QueuedIntegrationRun> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/shoprite/purchase-orders/refresh`, {
    method: "POST",
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to refresh Shoprite purchase orders: ${response.status}`);
  }

  return response.json();
}

export async function getPurchaseOrderFreshness(): Promise<ShopritePurchaseOrderFreshness> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/shoprite/purchase-orders/freshness`,
    { headers, cache: "no-store" },
  );

  if (!response.ok) {
    throw new Error(`Failed to load PO freshness: ${response.status}`);
  }

  return response.json();
}

export async function getIntegrationRuns(): Promise<IntegrationRun[]> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/integration-runs`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load integration runs: ${response.status}`);
  }

  return response.json();
}

export async function getIntegrationRun(id: string): Promise<IntegrationRun> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/integration-runs/${id}`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load integration run: ${response.status}`);
  }

  return response.json();
}

export async function getOperationsSummary(): Promise<OperationsSummary> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/operations/summary`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load operations summary: ${response.status}`);
  }

  return response.json();
}

export async function getAcumaticaPushNotificationEvents(): Promise<AcumaticaPushNotificationEventView> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/acumatica-events`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load Acumatica webhook events: ${response.status}`);
  }

  return response.json();
}

export async function seedInvoiceCandidateFromPurchaseOrder(
  id: string,
): Promise<InvoiceCandidateSummary> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/shoprite/purchase-orders/${id}/seed-test-invoice`,
    {
      method: "POST",
      headers,
      cache: "no-store",
    },
  );

  if (!response.ok) {
    throw new Error(`Failed to seed invoice candidate: ${response.status}`);
  }

  return response.json();
}

export async function getIntegrationMessages(): Promise<IntegrationMessageView> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/admin/integration-messages`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load integration messages: ${response.status}`);
  }

  return response.json();
}

export async function enqueueIntegrationCommand(
  command: "acumatica-discovery" | "shoprite-po-refresh",
): Promise<{ messageId: string }> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/admin/integration-messages/${command}`,
    { method: "POST", headers, cache: "no-store" },
  );

  if (!response.ok) {
    throw new Error(`Failed to queue integration command: ${response.status}`);
  }

  return response.json();
}

export type ExceptionTaskComment = {
  id: string;
  actor: string;
  body: string;
  createdAt: string;
};

export type ExceptionTask = {
  id: string;
  deduplicationKey: string;
  category: string;
  severity: string;
  status: string;
  entityType: string;
  entityId: string;
  invoiceCandidateId?: string;
  invoiceNumber?: string;
  errorCode: string;
  summary: string;
  fixLocation: string;
  retryClassification: string;
  owner?: string;
  occurrenceCount: number;
  latestEvidence?: string;
  firstSeenAt: string;
  lastSeenAt: string;
  dueAt?: string;
  isOverdue: boolean;
  resolvedAt?: string;
  resolvedBy?: string;
  resolutionReason?: string;
  comments: ExceptionTaskComment[];
};

export type ExceptionQueueSummary = {
  ambiguous: number;
  rejected: number;
  needsReview: number;
  deadLetters: number;
  stuck: number;
  held: number;
  overdue: number;
  resolved: number;
};

export type ExceptionQueueListing = {
  tasks: ExceptionTask[];
  summary: ExceptionQueueSummary;
};

export async function getExceptions(
  status?: string,
  category?: string,
): Promise<ExceptionQueueListing> {
  const headers = await getApiAuthHeaders();
  const query = new URLSearchParams();
  if (status) {
    query.set("status", status);
  }
  if (category) {
    query.set("category", category);
  }
  const suffix = query.size > 0 ? `?${query.toString()}` : "";
  const response = await fetch(`${apiBaseUrl}/api/exceptions${suffix}`, {
    headers,
    cache: "no-store",
  });

  if (!response.ok) {
    throw new Error(`Failed to load exceptions: ${response.status}`);
  }

  return response.json();
}

async function postException(path: string, body: unknown): Promise<void> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(`${apiBaseUrl}/api/exceptions/${path}`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });

  if (!response.ok) {
    const result = await readJson(response);
    throw new Error(getErrorMessage(result) ?? `The exception action failed: ${response.status}`);
  }
}

export function assignException(id: string, owner: string | null): Promise<void> {
  return postException(`${id}/assign`, { owner });
}

export function commentOnException(id: string, body: string): Promise<void> {
  return postException(`${id}/comments`, { body });
}

export function setExceptionStatus(id: string, status: string, reason: string): Promise<void> {
  return postException(`${id}/status`, { status, reason });
}

export function resolveAmbiguousSubmission(
  submissionOperationId: string,
  outcome: string,
  evidence: string,
  reason: string,
): Promise<void> {
  return postException(`ambiguous/${submissionOperationId}/resolve`, { outcome, evidence, reason });
}

export function holdInvoice(invoiceCandidateId: string, reason: string): Promise<void> {
  return postException(`invoices/${invoiceCandidateId}/hold`, { reason });
}

export function releaseInvoice(invoiceCandidateId: string, reason: string): Promise<void> {
  return postException(`invoices/${invoiceCandidateId}/release`, { reason });
}

export function retrySubmission(invoiceCandidateId: string, reason: string): Promise<void> {
  return postException(`invoices/${invoiceCandidateId}/retry`, { reason });
}

export function replayDeadLetter(deliveryId: string, reason: string): Promise<void> {
  return postException(`dead-letters/${deliveryId}/replay`, { reason });
}

export function resolveDeadLetters(
  reason: string,
  queueName: string | null,
  olderThanDays: number,
): Promise<void> {
  return postException("dead-letters/resolve", { reason, queueName, olderThanDays });
}

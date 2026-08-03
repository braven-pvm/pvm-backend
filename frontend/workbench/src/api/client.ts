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

export type PurchaseOrderRefreshResult = {
  received: number;
  created: number;
  updated: number;
  skipped: number;
  refreshedAt: string;
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

export async function saveInvoiceLineMapping(
  id: string,
  lineNumber: number,
  input: {
    purchaseOrderLineId: string;
    shopriteUom: "EA" | "CA" | "CS" | "KG";
  },
): Promise<InvoiceCandidateSummary> {
  const headers = await getApiAuthHeaders();
  const response = await fetch(
    `${apiBaseUrl}/api/invoices/${id}/line-mappings/${lineNumber}`,
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
    throw new Error(`Failed to save invoice line mapping: ${response.status}`);
  }

  return response.json();
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

export async function refreshPurchaseOrders(): Promise<PurchaseOrderRefreshResult> {
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

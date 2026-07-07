import { getApiAuthHeaders } from "../auth/session";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

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
  orderTypeCode?: string;
  orderTypeLabel?: string;
  deliveryGln?: string;
  deliveryLocationCode?: string;
  deliveryLocationName?: string;
  deliveryLocationSource: string;
  supplierGln?: string;
  lineCount: number;
  lastSeenAt: string;
};

export type PurchaseOrderDetail = PurchaseOrderSummary & {
  orderHeaderId?: string;
  buyerGln?: string;
  currencyCode?: string;
  totalExcludingTax?: number;
  totalIncludingTax?: number;
  totalTax?: number;
  sourceEnvironment: string;
  sourceEndpoint: string;
  payloadHash?: string;
  shopriteCreatedAt?: string;
  shopriteLastUpdatedAt?: string;
  firstSeenAt: string;
  lines: Array<{
    id: string;
    lineNumber: number;
    gtin?: string;
    buyerItemId?: string;
    buyerItemDescription?: string;
    supplierItemId?: string;
    description?: string;
    requestedQuantity?: number;
    measurementUnitCode?: string;
    netAmount?: number;
    netPrice?: number;
    monetaryAmountExcludingTaxes?: number;
    monetaryAmountIncludingTaxes?: number;
  }>;
  linkedInvoiceCandidates: Array<{
    id: string;
    invoiceNumber: string;
    customerAccount: string;
    status: string;
    updatedAt: string;
  }>;
  rawOrderJson?: string;
};

export type PurchaseOrderRefreshResult = {
  received: number;
  created: number;
  updated: number;
  skipped: number;
  refreshedAt: string;
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

export async function refreshInvoiceCandidates(): Promise<InvoiceCandidateSummary> {
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
  const result = (await response.json()) as SubmitInvoiceResult;

  if (!response.ok) {
    throw new Error(result.message ?? `Failed to submit invoice: ${response.status}`);
  }

  return result;
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

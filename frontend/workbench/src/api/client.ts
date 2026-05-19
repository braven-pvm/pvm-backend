import { getApiAuthHeaders } from "../auth/session";

const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export type InvoiceCandidateSummary = {
  id: string;
  invoiceNumber: string;
  customerAccount: string;
  customerLocation?: string;
  shopritePurchaseOrderNumber?: string;
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

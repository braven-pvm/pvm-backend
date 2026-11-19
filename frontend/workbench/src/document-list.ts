/*
 * The list a document page was opened from: the review list, the Documents
 * search with its filters, or the General scans list with its search. It
 * travels in the URL (?list=review&page=2, ?list=search&state=NeedsReview&page=3,
 * or ?list=general&q=eskom), never in browser storage, so
 * "Back to list", Previous and Next walk the same list the user saw.
 * Pure helpers: the node tests in document-list.test.mjs cover them.
 */

/** The search filters as the Documents page puts them in its URL. */
export type ListFilters = {
  invoice?: string;
  client?: string;
  type?: string;
  state?: string;
  /** yyyy-mm-dd */
  from?: string;
  /** yyyy-mm-dd */
  to?: string;
};

/** The General scans filters as its page puts them in its URL. */
export type GeneralListFilters = {
  q?: string;
  /** A scanner device ID. */
  device?: string;
  /** A staff name ID. */
  staff?: string;
  /** yyyy-mm-dd */
  from?: string;
  /** yyyy-mm-dd */
  to?: string;
};

export type ListContext =
  | { kind: "review"; page: number }
  | { kind: "search"; filters: ListFilters; page: number }
  | { kind: "general"; filters: GeneralListFilters; page: number };

/** A neighbour in the list, with the list page it is on. */
export type ListNeighbour = { id: string; page: number };

export type Neighbours = {
  previous: ListNeighbour | null;
  next: ListNeighbour | null;
  /** Counted from 1 across all pages. */
  position: number;
  total: number;
};

type Params = URLSearchParams | Record<string, string | string[] | undefined>;

const textFilters = ["invoice", "client", "type", "state"] as const;
const generalTextFilters = ["q", "device", "staff"] as const;
const dayFilters = ["from", "to"] as const;

function read(params: Params, key: string): string | undefined {
  if (params instanceof URLSearchParams) {
    return params.get(key) ?? undefined;
  }
  const value = params[key];
  return Array.isArray(value) ? value[0] : value;
}

function isDay(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const date = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(value);
}

function pageOf(value: string | undefined): number {
  const page = Number(value);
  return Number.isInteger(page) && page > 1 ? page : 1;
}

/** The list context from the document page's search params, or null for a direct link. */
export function parseListContext(params: Params): ListContext | null {
  const list = read(params, "list");
  const page = pageOf(read(params, "page"));
  if (list === "review") {
    return { kind: "review", page };
  }
  if (list === "general") {
    const filters: GeneralListFilters = {};
    for (const key of [...generalTextFilters, ...dayFilters]) {
      const value = read(params, key)?.trim();
      if (value && (!(dayFilters as readonly string[]).includes(key) || isDay(value))) {
        filters[key] = value;
      }
    }
    return { kind: "general", filters, page };
  }
  if (list !== "search") {
    return null;
  }
  const filters: ListFilters = {};
  for (const key of textFilters) {
    const value = read(params, key)?.trim();
    if (value) {
      filters[key] = value;
    }
  }
  for (const key of dayFilters) {
    const value = read(params, key)?.trim();
    if (value && isDay(value)) {
      filters[key] = value;
    }
  }
  return { kind: "search", filters, page };
}

/** The list's own query: the filters and the page, without "list". */
function listQuery(context: ListContext): URLSearchParams {
  const query = new URLSearchParams();
  if (context.kind === "search") {
    for (const key of [...textFilters, ...dayFilters]) {
      const value = context.filters[key];
      if (value) {
        query.set(key, value);
      }
    }
  }
  if (context.kind === "general") {
    for (const key of [...generalTextFilters, ...dayFilters]) {
      const value = context.filters[key];
      if (value) {
        query.set(key, value);
      }
    }
  }
  if (context.page > 1) {
    query.set("page", String(context.page));
  }
  return query;
}

function withQuery(path: string, query: URLSearchParams): string {
  const text = query.toString();
  return text ? `${path}?${text}` : path;
}

/** The list itself, with an optional notice such as { documentStatus: "Document filed to INV1." }. */
export function listHref(context: ListContext | null, notice: Record<string, string> = {}): string {
  const query = context ? listQuery(context) : new URLSearchParams();
  for (const [key, value] of Object.entries(notice)) {
    query.set(key, value);
  }
  const path = context?.kind === "review" ? "/documents/review" : context?.kind === "general" ? "/documents/general" : "/documents";
  return withQuery(path, query);
}

/** The name of the list for the back link. */
export function listLabel(context: ListContext | null): string {
  switch (context?.kind) {
    case "review":
      return "Review list";
    case "general":
      return "General scans";
    default:
      return "Documents";
  }
}

/** The context as name/value pairs: hidden form fields, or the start of a document link. */
export function listContextFields(context: ListContext | null): Record<string, string> {
  if (!context) {
    return {};
  }
  return { list: context.kind, ...Object.fromEntries(listQuery(context)) };
}

/** A document page that keeps the list context, with an optional notice. */
export function documentHref(id: string, context: ListContext | null, notice: Record<string, string> = {}): string {
  const query = new URLSearchParams(listContextFields(context));
  for (const [key, value] of Object.entries(notice)) {
    query.set(key, value);
  }
  return withQuery(`/documents/${id}`, query);
}

/** "3 of 12". */
export function positionText(position: number, total: number): string {
  return `${position} of ${total}`;
}

type ListPage = { items: { id: string }[]; pageSize: number; total: number };

/**
 * Finds the document in the list and the documents before and after it. It
 * starts on the page the user came from, then the pages next to it, then the
 * rest from the first; it loads at most maxPages pages. Null when the document
 * is not on the list (any more).
 */
export async function findNeighbours(
  id: string,
  startPage: number,
  fetchPage: (page: number) => Promise<ListPage>,
  maxPages = 20,
): Promise<Neighbours | null> {
  const loaded = new Map<number, ListPage>();
  const load = async (page: number) => {
    let result = loaded.get(page);
    if (!result) {
      result = await fetchPage(page);
      loaded.set(page, result);
    }
    return result;
  };

  const first = await load(startPage);
  const pageSize = Math.max(first.pageSize, 1);
  const pageCount = Math.max(Math.ceil(first.total / pageSize), 1);
  const order = [startPage, startPage - 1, startPage + 1, ...Array.from({ length: pageCount }, (_, index) => index + 1)];
  const tried = new Set<number>();

  for (const page of order) {
    if (page < 1 || page > pageCount || tried.has(page)) {
      continue;
    }
    if (!loaded.has(page) && loaded.size >= maxPages) {
      break;
    }
    tried.add(page);
    const current = await load(page);
    const index = current.items.findIndex((item) => item.id === id);
    if (index < 0) {
      continue;
    }

    let previous: ListNeighbour | null = null;
    if (index > 0) {
      previous = { id: current.items[index - 1].id, page };
    } else if (page > 1) {
      const before = (await load(page - 1)).items;
      previous = before.length > 0 ? { id: before[before.length - 1].id, page: page - 1 } : null;
    }

    let next: ListNeighbour | null = null;
    if (index < current.items.length - 1) {
      next = { id: current.items[index + 1].id, page };
    } else if (page < pageCount) {
      const after = (await load(page + 1)).items;
      next = after.length > 0 ? { id: after[0].id, page: page + 1 } : null;
    }

    return { previous, next, position: (page - 1) * pageSize + index + 1, total: current.total };
  }
  return null;
}

/**
 * Where an action sends the user once it worked.
 * leave "next": File-to and Reject. From the review list, the next document
 * opens (or the list, when there is none); from anywhere else the page stays.
 * leave "list": Delete. From the review list as "next"; otherwise the list.
 * leave "stay": the document stays where it is, such as Process again.
 */
export function afterActionHref({
  id,
  context,
  next,
  notice,
  leave,
}: {
  id: string;
  context: ListContext | null;
  next: ListNeighbour | null;
  notice: Record<string, string>;
  leave: "next" | "list" | "stay";
}): string {
  if (leave !== "stay" && context?.kind === "review") {
    return next ? documentHref(next.id, { kind: "review", page: next.page }, notice) : listHref(context, notice);
  }
  if (leave === "list") {
    return listHref(context, notice);
  }
  return documentHref(id, context, notice);
}

/**
 * The step a key press asks for on the document page: J or → for the next
 * document, K or ← for the previous one. Nothing while a field, an editable
 * area or a dialog has the keys, or with a modifier key held.
 */
export function keyboardStep(event: {
  key: string;
  /** The tag name of the focused element, such as "INPUT". */
  target: string;
  editable: boolean;
  modifier: boolean;
  dialogOpen: boolean;
}): "previous" | "next" | null {
  if (event.modifier || event.editable || event.dialogOpen) {
    return null;
  }
  if (["INPUT", "TEXTAREA", "SELECT"].includes(event.target.toUpperCase())) {
    return null;
  }
  switch (event.key) {
    case "j":
    case "J":
    case "ArrowRight":
      return "next";
    case "k":
    case "K":
    case "ArrowLeft":
      return "previous";
    default:
      return null;
  }
}

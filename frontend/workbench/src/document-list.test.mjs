import assert from "node:assert/strict";
import { test } from "node:test";
import {
  afterActionHref,
  documentHref,
  findNeighbours,
  keyboardStep,
  listContextFields,
  listHref,
  listLabel,
  parseListContext,
  positionText,
} from "./document-list.ts";

test("no list context on a direct link", () => {
  assert.equal(parseListContext({}), null);
  assert.equal(parseListContext({ list: "elsewhere" }), null);
  assert.equal(parseListContext({ q: "INV1" }), null);
});

test("the review list context keeps its page", () => {
  assert.deepEqual(parseListContext({ list: "review" }), { kind: "review", page: 1 });
  assert.deepEqual(parseListContext({ list: "review", page: "3" }), { kind: "review", page: 3 });
  assert.deepEqual(parseListContext({ list: "review", page: "-2" }), { kind: "review", page: 1 });
  assert.deepEqual(parseListContext({ list: "review", page: "abc" }), { kind: "review", page: 1 });
});

test("the search context keeps its filters and page", () => {
  assert.deepEqual(
    parseListContext({ list: "search", state: "NeedsReview", invoice: " INV1 ", from: "2026-11-01", to: "2026-11-17", page: "2", q: "ignored" }),
    { kind: "search", filters: { invoice: "INV1", state: "NeedsReview", from: "2026-11-01", to: "2026-11-17" }, page: 2 },
  );
  // A date that is not a day is dropped, like the list does.
  assert.deepEqual(parseListContext({ list: "search", from: "yesterday" }), { kind: "search", filters: {}, page: 1 });
});

test("the context is read from URLSearchParams and arrays too", () => {
  assert.deepEqual(parseListContext(new URLSearchParams("list=search&client=Acme&type=POD")), {
    kind: "search",
    filters: { client: "Acme", type: "POD" },
    page: 1,
  });
  assert.deepEqual(parseListContext({ list: ["review", "search"], page: ["2"] }), { kind: "review", page: 2 });
});

test("back to the list the user came from", () => {
  assert.equal(listHref(null), "/documents");
  assert.equal(listHref({ kind: "review", page: 1 }), "/documents/review");
  assert.equal(listHref({ kind: "review", page: 2 }), "/documents/review?page=2");
  assert.equal(listHref({ kind: "search", filters: {}, page: 1 }), "/documents");
  assert.equal(
    listHref({ kind: "search", filters: { state: "NeedsReview", from: "2026-11-01" }, page: 3 }),
    "/documents?state=NeedsReview&from=2026-11-01&page=3",
  );
  assert.equal(
    listHref({ kind: "review", page: 1 }, { documentStatus: "Document filed to INV1." }),
    "/documents/review?documentStatus=Document+filed+to+INV1.",
  );
  assert.equal(listHref(null, { documentStatus: "Done." }), "/documents?documentStatus=Done.");
});

test("the list label names the list", () => {
  assert.equal(listLabel(null), "Documents");
  assert.equal(listLabel({ kind: "review", page: 1 }), "Review list");
  assert.equal(listLabel({ kind: "search", filters: {}, page: 1 }), "Documents");
});

test("a document link carries the list context", () => {
  assert.equal(documentHref("d1", null), "/documents/d1");
  assert.equal(documentHref("d1", { kind: "review", page: 1 }), "/documents/d1?list=review");
  assert.equal(documentHref("d1", { kind: "review", page: 2 }), "/documents/d1?list=review&page=2");
  assert.equal(
    documentHref("d1", { kind: "search", filters: { client: "Acme & Co" }, page: 1 }),
    "/documents/d1?list=search&client=Acme+%26+Co",
  );
  assert.equal(
    documentHref("d1", { kind: "review", page: 1 }, { documentStatus: "Document rejected." }),
    "/documents/d1?list=review&documentStatus=Document+rejected.",
  );
  // A link and its parse round-trip.
  const context = { kind: "search", filters: { invoice: "INV1", state: "Filed", from: "2026-11-01", to: "2026-11-02", type: "POD" }, page: 4 };
  const href = documentHref("d1", context);
  assert.deepEqual(parseListContext(new URLSearchParams(href.split("?")[1])), context);
});

test("the context as hidden form fields", () => {
  assert.deepEqual(listContextFields(null), {});
  assert.deepEqual(listContextFields({ kind: "review", page: 2 }), { list: "review", page: "2" });
  assert.deepEqual(listContextFields({ kind: "search", filters: { state: "Filed" }, page: 1 }), { list: "search", state: "Filed" });
});

test("the position reads 3 of 12", () => {
  assert.equal(positionText(3, 12), "3 of 12");
  assert.equal(positionText(1, 1), "1 of 1");
  assert.equal(positionText(1200, 1500), "1200 of 1500");
});

function pagedList(ids, pageSize) {
  const calls = [];
  const fetchPage = async (page) => {
    calls.push(page);
    return { items: ids.slice((page - 1) * pageSize, page * pageSize).map((id) => ({ id })), page, pageSize, total: ids.length };
  };
  return { fetchPage, calls };
}

test("neighbours in the middle of a page", async () => {
  const { fetchPage, calls } = pagedList(["a", "b", "c", "d"], 50);
  assert.deepEqual(await findNeighbours("b", 1, fetchPage), {
    previous: { id: "a", page: 1 },
    next: { id: "c", page: 1 },
    position: 2,
    total: 4,
  });
  assert.deepEqual(calls, [1]);
});

test("the first and the last document have one neighbour", async () => {
  const { fetchPage } = pagedList(["a", "b", "c"], 50);
  assert.deepEqual(await findNeighbours("a", 1, fetchPage), { previous: null, next: { id: "b", page: 1 }, position: 1, total: 3 });
  assert.deepEqual(await findNeighbours("c", 1, fetchPage), { previous: { id: "b", page: 1 }, next: null, position: 3, total: 3 });
});

test("neighbours across a page break", async () => {
  const { fetchPage, calls } = pagedList(["a", "b", "c", "d", "e"], 2);
  assert.deepEqual(await findNeighbours("c", 2, fetchPage), {
    previous: { id: "b", page: 1 },
    next: { id: "d", page: 2 },
    position: 3,
    total: 5,
  });
  assert.deepEqual(await findNeighbours("d", 2, fetchPage), {
    previous: { id: "c", page: 2 },
    next: { id: "e", page: 3 },
    position: 4,
    total: 5,
  });
  assert.ok(calls.every((page) => page >= 1 && page <= 3));
});

test("a document that moved to another page is still found", async () => {
  const { fetchPage } = pagedList(["a", "b", "c", "d", "e", "f", "g"], 2);
  // The user came from page 1, but the document is now on page 4.
  assert.deepEqual(await findNeighbours("g", 1, fetchPage), { previous: { id: "f", page: 3 }, next: null, position: 7, total: 7 });
});

test("a document that is not on the list has no neighbours", async () => {
  const { fetchPage } = pagedList(["a", "b"], 50);
  assert.equal(await findNeighbours("z", 1, fetchPage), null);
  const empty = pagedList([], 50);
  assert.equal(await findNeighbours("z", 3, empty.fetchPage), null);
});

test("the search for neighbours stops after a number of pages", async () => {
  const ids = Array.from({ length: 100 }, (_, index) => `id${index}`);
  const { fetchPage, calls } = pagedList(ids, 2);
  assert.equal(await findNeighbours("id99", 1, fetchPage, 5), null);
  assert.ok(calls.length <= 5);
});

test("after File-to, Reject or Delete in the review list, the next document opens", () => {
  const review = { kind: "review", page: 1 };
  const status = { documentStatus: "Document filed to INV1." };
  assert.equal(
    afterActionHref({ id: "d1", context: review, next: { id: "d2", page: 1 }, notice: status, leave: "next" }),
    "/documents/d2?list=review&documentStatus=Document+filed+to+INV1.",
  );
  assert.equal(
    afterActionHref({ id: "d1", context: { kind: "review", page: 2 }, next: { id: "d9", page: 3 }, notice: status, leave: "next" }),
    "/documents/d9?list=review&page=3&documentStatus=Document+filed+to+INV1.",
  );
  // No next: back to the list with the notice.
  assert.equal(
    afterActionHref({ id: "d1", context: review, next: null, notice: status, leave: "next" }),
    "/documents/review?documentStatus=Document+filed+to+INV1.",
  );
});

test("after an action from a search, the page stays, and Delete goes to the list", () => {
  const search = { kind: "search", filters: { state: "NeedsReview" }, page: 1 };
  const status = { documentStatus: "Document rejected." };
  assert.equal(
    afterActionHref({ id: "d1", context: search, next: { id: "d2", page: 1 }, notice: status, leave: "next" }),
    "/documents/d1?list=search&state=NeedsReview&documentStatus=Document+rejected.",
  );
  assert.equal(
    afterActionHref({ id: "d1", context: search, next: { id: "d2", page: 1 }, notice: status, leave: "list" }),
    "/documents?state=NeedsReview&documentStatus=Document+rejected.",
  );
  assert.equal(afterActionHref({ id: "d1", context: null, next: null, notice: status, leave: "stay" }), "/documents/d1?documentStatus=Document+rejected.");
  assert.equal(afterActionHref({ id: "d1", context: null, next: null, notice: status, leave: "list" }), "/documents?documentStatus=Document+rejected.");
  // An action that keeps the document (Process again) stays, in the review list too.
  assert.equal(
    afterActionHref({ id: "d1", context: { kind: "review", page: 1 }, next: { id: "d2", page: 1 }, notice: status, leave: "stay" }),
    "/documents/d1?list=review&documentStatus=Document+rejected.",
  );
});

test("J, K and the arrow keys move only when nothing else has the keys", () => {
  const key = (overrides) => ({ key: "j", target: "BODY", editable: false, modifier: false, dialogOpen: false, ...overrides });
  assert.equal(keyboardStep(key({})), "next");
  assert.equal(keyboardStep(key({ key: "J" })), "next");
  assert.equal(keyboardStep(key({ key: "ArrowRight" })), "next");
  assert.equal(keyboardStep(key({ key: "k" })), "previous");
  assert.equal(keyboardStep(key({ key: "ArrowLeft" })), "previous");
  assert.equal(keyboardStep(key({ key: "x" })), null);
  assert.equal(keyboardStep(key({ target: "INPUT" })), null);
  assert.equal(keyboardStep(key({ target: "TEXTAREA" })), null);
  assert.equal(keyboardStep(key({ target: "SELECT" })), null);
  assert.equal(keyboardStep(key({ editable: true })), null);
  assert.equal(keyboardStep(key({ modifier: true })), null);
  assert.equal(keyboardStep(key({ dialogOpen: true })), null);
  // A link or a button with focus does not use the arrows itself.
  assert.equal(keyboardStep(key({ target: "A", key: "ArrowLeft" })), "previous");
});

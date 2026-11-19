import assert from "node:assert/strict";
import { test } from "node:test";
import { isCurrent, navigationFor } from "./navigation.ts";

const labels = (groups) => groups.flatMap((group) => group.items.map((item) => item.label));

test("a viewer sees no write or admin items", () => {
  const groups = navigationFor(["Viewer"]);
  assert.ok(!labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
  assert.ok(!labels(groups).includes("Events"));
});

test("a viewer sees the stock sync", () => {
  assert.ok(labels(navigationFor(["Viewer"])).includes("Stock"));
});

test("an operator sees banking but not admin settings", () => {
  const groups = navigationFor(["Operator"]);
  assert.ok(labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
});

test("an admin sees everything", () => {
  const groups = navigationFor(["Admin"]);
  for (const label of ["Today", "Exceptions", "Shoprite", "Banking", "Stock", "Sell-out", "Statements", "Sales report", "Documents", "Automations", "Runs", "Events", "Users"]) {
    assert.ok(labels(groups).includes(label), label);
  }
});

test("a group with nothing allowed disappears", () => {
  const groups = navigationFor([]);
  assert.ok(!groups.some((group) => group.label === "Settings"));
});

test("home is current only on the home page", () => {
  assert.equal(isCurrent({ href: "/" }, "/"), true);
  assert.equal(isCurrent({ href: "/" }, "/shoprite/invoices"), false);
});

test("a section matches its own pages and its extra prefixes", () => {
  const shoprite = { href: "/shoprite/invoices", matches: ["/shoprite/invoices", "/shoprite/purchase-orders"] };
  assert.equal(isCurrent(shoprite, "/shoprite/invoices/abc"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/purchase-orders"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/invoices-archive"), false);
  assert.equal(isCurrent(shoprite, "/banking"), false);
});

test("the Shoprite entry is current on every page of the module", () => {
  const shoprite = navigationFor(["Admin"])
    .flatMap((group) => group.items)
    .find((item) => item.label === "Shoprite");
  assert.equal(shoprite.href, "/shoprite");
  assert.equal(isCurrent(shoprite, "/shoprite/invoices/abc"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/settings/inventory-mappings"), true);
  assert.equal(isCurrent(shoprite, "/banking"), false);
});

test("every signed-in user sees Documents with the review list and General scans under it", () => {
  const documents = navigationFor(["Viewer"])
    .flatMap((group) => group.items)
    .find((item) => item.label === "Documents");
  assert.ok(documents);
  assert.equal(documents.href, "/documents");
  assert.deepEqual(documents.children?.map((child) => child.href), ["/documents/review", "/documents/general"]);
});

test("the review list, not the search, is current on the review page", () => {
  const documents = navigationFor(["Viewer"])
    .flatMap((group) => group.items)
    .find((item) => item.label === "Documents");
  assert.equal(isCurrent(documents, "/documents/review"), true);
  assert.equal(isCurrent(documents.children[0], "/documents/review"), true);
  assert.equal(isCurrent(documents.children[0], "/documents"), false);
  assert.equal(isCurrent(documents.children[1], "/documents/general"), true);
  assert.equal(isCurrent(documents.children[1], "/documents/review"), false);
  assert.equal(isCurrent(documents, "/documents/6f1d0c2a-0001-4a7e-9a10-000000000001"), true);
});

test("only an admin sees the scanner devices link under Documents", () => {
  const documentsFor = (roles) =>
    navigationFor(roles)
      .flatMap((group) => group.items)
      .find((item) => item.label === "Documents");
  const childLabels = (roles) => (documentsFor(roles)?.children ?? []).map((child) => child.label);
  assert.deepEqual(childLabels(["Admin"]), ["Review list", "General scans", "Scanner devices"]);
  assert.deepEqual(childLabels(["Operator"]), ["Review list", "General scans"]);
  assert.deepEqual(childLabels(["Viewer"]), ["Review list", "General scans"]);
});

test("Operations lists Statements before Documents, and a viewer sees Documents but not Statements", () => {
  const operations = (roles) => navigationFor(roles).find((group) => group.label === "Operations").items.map((item) => item.label);
  assert.deepEqual(operations(["Admin"]), ["Shoprite", "Banking", "Stock", "Sell-out", "Statements", "Sales report", "Documents"]);
  assert.ok(!operations(["Viewer"]).includes("Statements"));
  assert.ok(operations(["Viewer"]).includes("Documents"));
});

test("a viewer does not see the sales report", () => {
  assert.ok(!labels(navigationFor(["Viewer"])).includes("Sales report"));
  assert.ok(labels(navigationFor(["Operator"])).includes("Sales report"));
});

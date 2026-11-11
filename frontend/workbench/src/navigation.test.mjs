import assert from "node:assert/strict";
import { test } from "node:test";
import { isCurrent, navigationFor } from "./navigation.ts";

const labels = (groups) => groups.flatMap((group) => group.items.map((item) => item.label));
const childLabels = (groups, parent) =>
  groups.flatMap((group) => group.items).find((item) => item.label === parent)?.children?.map((c) => c.label);

test("a viewer sees no write or admin items", () => {
  const groups = navigationFor(["Viewer"]);
  assert.ok(!labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
  assert.ok(!childLabels(groups, "Shoprite").includes("Inventory mappings"));
});

test("an operator sees banking but not admin settings", () => {
  const groups = navigationFor(["Operator"]);
  assert.ok(labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
});

test("an admin sees everything", () => {
  const groups = navigationFor(["Admin"]);
  for (const label of ["Today", "Exceptions", "Shoprite", "Banking", "Automations", "Runs", "Events", "Users"]) {
    assert.ok(labels(groups).includes(label), label);
  }
  assert.ok(childLabels(groups, "Shoprite").includes("Inventory mappings"));
});

test("a group with nothing allowed disappears", () => {
  const groups = navigationFor([]);
  assert.ok(!groups.some((group) => group.label === "Settings"));
});

test("home is current only on the home page", () => {
  assert.equal(isCurrent({ href: "/" }, "/"), true);
  assert.equal(isCurrent({ href: "/" }, "/invoices"), false);
});

test("a section matches its own pages and its extra prefixes", () => {
  const shoprite = { href: "/invoices", matches: ["/invoices", "/purchase-orders"] };
  assert.equal(isCurrent(shoprite, "/invoices/abc"), true);
  assert.equal(isCurrent(shoprite, "/purchase-orders"), true);
  assert.equal(isCurrent(shoprite, "/invoices-archive"), false);
  assert.equal(isCurrent(shoprite, "/banking"), false);
});

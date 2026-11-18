// Plain words for the sell-out import. Kept apart from the pages so that node --test can check them.

/** The plain reason why the import refused a file. The API gives a code, sometimes with detail after a colon. */
export function describeSellOutRefusal(reason) {
  const [code, ...rest] = String(reason ?? "").split(":");
  const detail = rest.join(":").trim();
  switch (code) {
    case "already-loaded":
      return "This exact file is loaded already. Nothing changed.";
    case "unknown-format":
      return "The import does not know this file. Send the Makro Dispo report, the Shoprite group portal report, or one wholesaler list.";
    case "several-formats":
      return "The file holds more than one wholesaler list. Save each list as its own file, then import each one.";
    case "bad-format":
      return detail ? `The file does not have its usual shape: ${detail}` : "The file does not have its usual shape.";
    case "too-many-bad-rows":
      return "More than 5 of each 100 rows could not be read, so nothing was loaded. Check the rows below.";
    default:
      return reason ? String(reason) : "The file was not loaded.";
  }
}

/** "2026-06-01" and "2026-11-01" become "Jun 2026 to Nov 2026". */
export function describeMonths(months) {
  if (!months || months.length === 0) {
    return "—";
  }

  const label = (value) =>
    new Date(`${String(value).slice(0, 10)}T00:00:00Z`).toLocaleDateString("en-ZA", { month: "short", year: "numeric", timeZone: "UTC" });
  const first = label(months[0]);
  const last = label(months[months.length - 1]);
  return first === last ? first : `${first} to ${last}`;
}

/** "upload:person@example.com" becomes "person@example.com", and "mail:x" becomes "x (mail)". */
export function describeReceivedBy(receivedBy) {
  const [how, ...rest] = String(receivedBy ?? "").split(":");
  const who = rest.join(":");
  if (how === "mail") {
    return `${who} (mail)`;
  }
  return who || String(receivedBy ?? "");
}

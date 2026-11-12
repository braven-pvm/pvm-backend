export function formatMoney(currencyCode, amount) {
  if (amount === undefined || amount === null) {
    return "-";
  }

  return `${currencyCode ?? "ZAR"} ${amount.toFixed(2)}`;
}


const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

// Rands with a space between thousands, the convention people read on a bank statement.
export function formatMoneyZar(amount) {
  if (amount === null || amount === undefined || Number.isNaN(Number(amount))) {
    return "—";
  }

  const value = Number(amount);
  const sign = value < 0 ? "-" : "";
  const [whole, cents] = Math.abs(value).toFixed(2).split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, " ");

  return `${sign}R ${grouped}.${cents}`;
}

// Dates arrive as plain "yyyy-MM-dd" from the API. Read them in UTC so the rendered day
// never shifts with the reader's timezone.
function parseIsoDate(iso) {
  if (typeof iso !== "string" || iso.length < 10) {
    return null;
  }

  const date = new Date(`${iso.slice(0, 10)}T00:00:00Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function formatStatementDate(iso) {
  const date = parseIsoDate(iso);
  if (!date) {
    return "—";
  }

  return `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
}

// Naming the weekday lets a reader see at once that an uncovered day was a weekend.
export function formatDayAndDate(iso) {
  const date = parseIsoDate(iso);
  if (!date) {
    return "—";
  }

  return `${DAYS[date.getUTCDay()]} ${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]}`;
}

const SHORT_MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

// Date and time parts in South African time, whatever the server's own time zone is.
function sastParts(iso) {
  if (!iso) {
    return null;
  }

  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return null;
  }

  // South Africa has no daylight saving, so a fixed +2 hours is exact.
  const shifted = new Date(date.getTime() + 2 * 60 * 60 * 1000);
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth(),
    day: shifted.getUTCDate(),
    hours: String(shifted.getUTCHours()).padStart(2, "0"),
    minutes: String(shifted.getUTCMinutes()).padStart(2, "0"),
  };
}

// "11 Nov, 05:00" in the current year, "11 Nov 2025, 05:00" in another year.
export function formatDateTime(iso, now = new Date()) {
  const parts = sastParts(iso);
  if (!parts) {
    return "—";
  }

  const current = sastParts(now.toISOString());
  const year = current && current.year === parts.year ? "" : ` ${parts.year}`;
  return `${parts.day} ${SHORT_MONTHS[parts.month]}${year}, ${parts.hours}:${parts.minutes}`;
}

// "11 Nov 2026".
export function formatDate(iso) {
  const parts = sastParts(iso);
  if (!parts) {
    return "—";
  }

  return `${parts.day} ${SHORT_MONTHS[parts.month]} ${parts.year}`;
}

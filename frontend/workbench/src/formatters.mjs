export function formatMoney(currencyCode, amount) {
  if (amount === undefined || amount === null) {
    return "-";
  }

  return `${currencyCode ?? "ZAR"} ${amount.toFixed(2)}`;
}

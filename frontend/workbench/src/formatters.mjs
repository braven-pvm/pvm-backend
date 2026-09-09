export function formatMoney(currencyCode, amount) {
  if (amount === undefined || amount === null) {
    return "-";
  }

  return `${currencyCode ?? "ZAR"} ${amount.toFixed(2)}`;
}

export function describeNedbankImport(result) {
  const fileName = result?.fileName ?? "The statement";
  const linesImported = result?.linesImported ?? 0;

  if (linesImported === 0) {
    return `${fileName} contained no new transactions. Acumatica was not changed.`;
  }

  const noun = linesImported === 1 ? "transaction" : "transactions";
  const reference = result?.statementReference;

  return reference
    ? `${fileName}: imported ${linesImported} ${noun} as statement ${reference}.`
    : `${fileName}: imported ${linesImported} ${noun}.`;
}

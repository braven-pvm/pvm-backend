namespace Pvm.Application.Banking;

/// <summary>
/// The statement names a bank account that is not mapped to an Acumatica cash account.
/// The import refuses rather than guessing: posting one account's statement into another is
/// silent and expensive to undo, and it has already happened once by hand.
/// </summary>
public sealed class UnknownBankAccountException(string message) : Exception(message);

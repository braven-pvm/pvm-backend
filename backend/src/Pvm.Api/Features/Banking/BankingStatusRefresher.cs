using Microsoft.Extensions.Options;
using Pvm.Application.Banking;
using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Nedbank;

namespace Pvm.Api.Features.Banking;

/// <summary>How often the API reads the banking status from Acumatica.</summary>
public sealed record BankingStatusOptions
{
    public const string SectionName = "BankingStatus";

    /// <summary>The time between two background reads.</summary>
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(150);
}

/// <summary>
/// Holds the banking status and reads it from Acumatica in the background, so a page visit
/// never waits for Acumatica. Only a visit before the first read of this API instance waits,
/// for that one read. An import refreshes the status before it answers, so the page then shows
/// the new statement.
/// </summary>
/// <remarks>
/// When an account read fails, the account keeps the figures of its last good read, marked stale
/// with the time of that read, and the failure is logged with the cash account and the reason. Reads run one at a time, so a read
/// that started earlier never replaces the result of a later one.
/// </remarks>
public sealed class BankingStatusRefresher(
    IServiceScopeFactory scopes,
    IOptions<NedbankOptions> nedbankOptions,
    IOptions<InvestecOptions> investecOptions,
    IOptions<BankingStatusOptions> options,
    TimeProvider timeProvider,
    ILogger<BankingStatusRefresher> logger) : BackgroundService
{
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private volatile BankingStatusResponse? _current;

    /// <summary>The stored status. Only before the first read does it read and wait.</summary>
    public async Task<BankingStatusResponse> GetAsync(CancellationToken cancellationToken)
    {
        if (_current is { } current)
        {
            return current;
        }

        await _readLock.WaitAsync(cancellationToken);
        try
        {
            return _current ?? await ReadAndStoreAsync(cancellationToken);
        }
        finally
        {
            _readLock.Release();
        }
    }

    /// <summary>Reads the status now and stores it. Call it after anything is imported.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _readLock.WaitAsync(cancellationToken);
        try
        {
            await ReadAndStoreAsync(cancellationToken);
        }
        finally
        {
            _readLock.Release();
        }
    }

    /// <summary>
    /// Refreshes after an import. It never throws: the import already happened, so a failed
    /// status read must not turn its answer into an error. The next background read tries again.
    /// </summary>
    public async Task TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "banking.status.refresh.failed After=import");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.RefreshInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The page keeps the last status. The next round tries again.
                logger.LogError(exception, "banking.status.refresh.failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<BankingStatusResponse> ReadAndStoreAsync(CancellationToken cancellationToken)
    {
        var readAt = timeProvider.GetUtcNow();
        BankingStatusRead read;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var client = scope.ServiceProvider.GetRequiredService<IAcumaticaBankStatementClient>();
            read = await BankImportEndpoints.ReadStatusAsync(
                client, nedbankOptions.Value, investecOptions.Value, cancellationToken);
        }

        foreach (var (cashAccount, reason) in read.Failures)
        {
            logger.LogWarning(
                "banking.status.account.failed CashAccount={CashAccount} Reason={Reason}",
                cashAccount,
                reason);
        }

        var previous = _current?.Accounts.Where(account => account.Available)
            .GroupBy(account => account.CashAccount, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var accounts = read.Response.Accounts
            .Select(account => read.Failures.ContainsKey(account.CashAccount)
                ? previous?.GetValueOrDefault(account.CashAccount) is { } lastGood
                    ? lastGood with { Stale = true }
                    : account
                : account with { ReadAt = readAt })
            .ToList();
        var status = new BankingStatusResponse(accounts);
        _current = status;
        return status;
    }
}

using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The single home of the whole-call deadline. Every Maxio call an API request makes runs under one token from
/// <see cref="Start"/>, so the caller-visible ceiling is <c>Maxio:OperationBudgetSeconds</c> no matter how many
/// SDK calls (and SDK retries) the request performs.
/// </summary>
public class MaxioCallBudget
{
    private readonly TimeSpan _operationBudget;
    private readonly TimeSpan _settlementBudget;

    public MaxioCallBudget(IOptions<MaxioOptions> options)
    {
        _operationBudget = TimeSpan.FromSeconds(options.Value.OperationBudgetSeconds);
        _settlementBudget = TimeSpan.FromSeconds(options.Value.AttemptTimeoutSeconds);
    }

    /// <summary>A deadline for the request's Maxio work, also cancelled when the HTTP caller disconnects.</summary>
    public CancellationTokenSource Start(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(_operationBudget);
        return cts;
    }

    /// <summary>
    /// A fresh, short deadline for re-reading provider state after a write's outcome became unknown — used when
    /// the request's own token may already be spent. Deliberately not linked to the request so a disconnecting
    /// caller does not leave the claim unsettled.
    /// </summary>
    public CancellationTokenSource StartSettlement() => new(_settlementBudget);

    /// <summary>How long an unsettled claim must sit before another request may take it over.</summary>
    public TimeSpan StaleClaimAfter => _operationBudget + _operationBudget;
}

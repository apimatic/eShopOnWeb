using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

/// <summary>
/// The total time one unit of work (an API request, or one sweeper item) may spend waiting on PayPal.
/// Scoped, so every PayPal call made while serving one request shares one deadline: per-call timeouts
/// cannot add up past it.
/// </summary>
public sealed class PayPalRequestBudget : IDisposable
{
    private readonly CancellationTokenSource _cts;

    public PayPalRequestBudget(IOptions<PayPalOptions> options, TimeProvider clock)
    {
        _cts = new CancellationTokenSource(options.Value.RequestBudget, clock);
    }

    public CancellationToken Token => _cts.Token;
    public bool IsExhausted => _cts.IsCancellationRequested;

    public void Dispose() => _cts.Dispose();
}

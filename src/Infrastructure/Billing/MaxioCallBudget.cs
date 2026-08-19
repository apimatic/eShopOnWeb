using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public interface IMaxioCallBudget
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken);
}

internal sealed class MaxioCallBudget : IMaxioCallBudget
{
    private readonly TimeSpan _budget;

    public MaxioCallBudget(IOptions<MaxioOptions> options)
    {
        _ = options;
        _budget = TimeSpan.FromSeconds(30);
    }

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        return await call(cts.Token);
    }
}

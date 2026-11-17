using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class EndpointBudget
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(35);

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        return await action(budget.Token);
    }

    public static async Task RunAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        await action(budget.Token);
    }
}

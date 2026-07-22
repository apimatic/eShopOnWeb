using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Verifies at startup that the configured metered component resolves to a metered-kind component
/// on the configured product family, so a bad seed is visible immediately rather than at the first
/// usage report.
/// </summary>
/// <remarks>
/// The result is reported, never enforced: eShopOnWeb must start and serve its catalogue, basket,
/// and checkout even when the billing provider is unreachable or misconfigured. The same check runs
/// again — and does block — before every usage report, which is where it actually matters.
/// </remarks>
public class SubscriptionCatalogValidator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SubscriptionCatalogValidator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Both the domain service and eShopOnWeb's logger adapter are scoped, so everything this
        // check needs is resolved from a scope of its own rather than injected into the singleton.
        using var scope = _scopeFactory.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IAppLogger<SubscriptionCatalogValidator>>();

        try
        {
            var subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

            var component = await subscriptions.GetMeteredComponentAsync(cancellationToken);

            logger.LogInformation(
                $"Billing catalogue verified: metered component '{component.Handle}' (id {component.Id}) " +
                $"is available at {component.UnitPrice:C} per unit.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "The billing catalogue could not be verified at startup, so pay-as-you-go usage will be " +
                $"refused until it is corrected. eShopOnWeb is otherwise unaffected. {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

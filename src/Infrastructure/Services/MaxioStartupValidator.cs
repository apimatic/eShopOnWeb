using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Confirms at startup that the configured Maxio catalog resolves and that the configured usage component
/// really is metered — the UC2 precondition (plan.md UC2, Phase 3).
/// </summary>
/// <remarks>
/// This validator only reports. A billing misconfiguration or an unreachable provider must never stop
/// eShopOnWeb's catalog, basket and order flows from starting, so every failure is logged and swallowed.
/// The same checks run again inside the service before any usage is recorded, so nothing depends on this
/// pass having succeeded.
/// <para>
/// Hosted services are singletons while <see cref="IAppLogger{T}"/> and <see cref="IBillingClient"/> are
/// not, so both are resolved from a scope created inside <see cref="StartAsync"/> rather than injected.
/// </para>
/// </remarks>
public class MaxioStartupValidator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MaxioSettings _settings;

    public MaxioStartupValidator(IServiceScopeFactory scopeFactory, IOptions<MaxioSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IAppLogger<MaxioStartupValidator>>();

        try
        {
            _settings.Validate();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Maxio billing is not configured; subscription features will be unavailable. {0}",
                ex.Message);
            return;
        }

        logger.LogInformation("Maxio billing target: {0} ({1}).",
            _settings.ResolveBaseUrl(),
            _settings.HasExplicitBaseUrl ? "explicit Maxio:BaseUrl" : "derived from Maxio:Subdomain");

        try
        {
            var billingClient = scope.ServiceProvider.GetRequiredService<IBillingClient>();

            var plans = await billingClient.ListPlansAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Maxio product family '{0}' resolved with {1} plan(s).",
                _settings.ProductFamilyHandle, plans.Count);

            var component = await billingClient
                .FindComponentByHandleAsync(_settings.MeteredComponentHandle, cancellationToken)
                .ConfigureAwait(false);

            if (component is null)
            {
                logger.LogWarning(
                    "Maxio component '{0}' does not exist on product family '{1}'. Pay-as-you-go usage will " +
                    "be refused until the sandbox is seeded.",
                    _settings.MeteredComponentHandle, _settings.ProductFamilyHandle);
            }
            else if (!component.IsMetered)
            {
                logger.LogWarning(
                    "Maxio component '{0}' is of kind '{1}', not metered. Usage will be refused; archive it " +
                    "and recreate it as metered.",
                    _settings.MeteredComponentHandle, component.Kind);
            }
            else
            {
                logger.LogInformation("Maxio metered component '{0}' (id {1}) verified at {2} per unit.",
                    component.Handle,
                    component.Id,
                    component.UnitPrice.HasValue
                        ? BillingMoney.ToDisplay(component.UnitPrice.Value)
                        : "an unpublished price");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Maxio startup validation could not reach the provider: {0}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

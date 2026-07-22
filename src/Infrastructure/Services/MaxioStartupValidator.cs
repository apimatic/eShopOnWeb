using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Checks at startup that the configured metered component really exists and is of metered kind,
/// so a bad seed is visible in the logs rather than only when a customer first reports usage
/// (plan.md UC2 preconditions).
/// </summary>
/// <remarks>
/// <para>
/// This is a diagnostic, not a gate. The check that actually protects billing runs immediately
/// before every usage write; this one only makes the problem obvious sooner.
/// </para>
/// <para>
/// It therefore never throws and never delays startup: the work runs detached, and a host with no
/// Maxio configuration at all — a test host, for instance — skips it silently. Nothing about
/// eShopOnWeb's own startup may depend on the billing provider being reachable.
/// </para>
/// </remarks>
public class MaxioStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MaxioSettings _settings;

    // A hosted service is a singleton, so nothing scoped may be injected here. The billing client
    // and the logger are both resolved from a scope created when the check actually runs.
    public MaxioStartupValidator(IServiceProvider serviceProvider, IOptions<MaxioSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            // Not configured for this host; there is nothing to check and nothing to warn about.
            return Task.CompletedTask;
        }

        // Detached on purpose: an unreachable provider must not hold up the application starting.
        _ = Task.Run(() => ValidateAsync(cancellationToken), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        IAppLogger<MaxioStartupValidator>? logger = null;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            logger = scope.ServiceProvider.GetRequiredService<IAppLogger<MaxioStartupValidator>>();

            var component = await scope.ServiceProvider
                .GetRequiredService<IBillingClient>()
                .GetMeteredComponentAsync(cancellationToken);

            logger.LogInformation(
                "Maxio pay-as-you-go component '{0}' (id {1}) verified as metered at {2:N2} per unit.",
                component.Handle,
                component.Id,
                component.UnitPrice);
        }
        catch (BillingConfigurationException ex)
        {
            logger?.LogWarning(
                "Maxio pay-as-you-go usage is not available: {0} Usage reporting will be refused until this is corrected.",
                ex.Message);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                "Could not verify the Maxio pay-as-you-go component at startup: {0} " +
                "This will be re-checked before the first usage is recorded.",
                ex.Message);
        }
    }
}

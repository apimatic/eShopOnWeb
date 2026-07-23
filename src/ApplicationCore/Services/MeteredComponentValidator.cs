using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Resolves and validates the configured metered component once, then serves the confirmed result
/// from memory. A failed validation is never cached, so correcting the seed (UC0) takes effect
/// without restarting the host.
/// </summary>
public class MeteredComponentValidator : IMeteredComponentValidator
{
    private readonly IBillingClient _billingClient;
    private readonly SubscriptionSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MeteredComponent? _validated;

    public MeteredComponentValidator(IBillingClient billingClient, IOptions<SubscriptionSettings> settings)
    {
        _billingClient = billingClient;
        _settings = settings.Value;
    }

    public async Task<MeteredComponent> GetValidatedComponentAsync(CancellationToken cancellationToken = default)
    {
        var cached = _validated;
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_validated is not null)
            {
                return _validated;
            }

            var handle = _settings.MeteredComponentHandle;
            if (string.IsNullOrWhiteSpace(handle))
            {
                throw new BillingConfigurationException(
                    "No metered component handle is configured (Maxio:MeteredComponentHandle). Usage cannot be recorded.");
            }

            var component = await _billingClient.FindComponentByHandleAsync(handle, cancellationToken);
            if (component is null)
            {
                throw new BillingConfigurationException(
                    $"The configured metered component handle '{handle}' does not resolve on product family " +
                    $"'{_settings.ProductFamilyHandle}'. Correct the billing seed (UC0) before recording usage.");
            }

            if (!component.IsMetered)
            {
                throw new BillingConfigurationException(
                    $"The component '{handle}' is of kind '{component.Kind ?? "unknown"}', not " +
                    $"'{MeteredComponent.MeteredKind}'. A component's kind cannot be changed in place — archive it " +
                    "and recreate it as metered (UC0) before recording usage.");
            }

            _validated = component;
            return component;
        }
        finally
        {
            _gate.Release();
        }
    }
}

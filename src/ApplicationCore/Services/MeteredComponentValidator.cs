using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Caches a successful metered-component validation for the lifetime of the process. Registered
/// as a singleton; the billing client is passed in per call so this never captures a scoped
/// dependency.
/// </summary>
public class MeteredComponentValidator : IMeteredComponentValidator
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private string? _validatedHandle;

    public async Task EnsureComponentIsMeteredAsync(IBillingClient billingClient,
        string componentHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(_validatedHandle, componentHandle, System.StringComparison.Ordinal))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: a concurrent caller may have validated while we waited.
            if (string.Equals(_validatedHandle, componentHandle, System.StringComparison.Ordinal))
            {
                return;
            }

            var component = await billingClient.FindComponentByHandleAsync(componentHandle, cancellationToken);

            if (component is null)
            {
                throw new BillingConfigurationException(
                    $"The configured usage component '{componentHandle}' does not exist on the configured " +
                    "product family. Re-run the billing provider seed before reporting usage.");
            }

            if (!component.IsMetered)
            {
                throw new BillingConfigurationException(
                    $"The configured usage component '{componentHandle}' is of kind '{component.Kind}', not a " +
                    "metered component. A component's kind cannot be changed in place — archive it and " +
                    "recreate it as metered.");
            }

            _validatedHandle = componentHandle;
        }
        finally
        {
            _gate.Release();
        }
    }
}

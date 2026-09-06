using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads validated <see cref="MaxioSettings"/> and reports a missing or malformed configuration
/// as a billing failure rather than as a raw options exception, so the API layer can turn it into
/// a meaningful response.
/// </summary>
internal static class MaxioOptionsAccessor
{
    public static MaxioSettings Resolve(IOptions<MaxioSettings> options)
    {
        try
        {
            return options.Value;
        }
        catch (OptionsValidationException ex)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured: " + string.Join(" ", ex.Failures), ex);
        }
    }
}

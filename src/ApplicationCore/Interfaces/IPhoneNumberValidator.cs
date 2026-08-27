using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's
/// canonical form of it.
/// </summary>
public interface IPhoneNumberValidator
{
    /// <summary>
    /// Returns the provider's canonical (E.164) form of the number, or null when the
    /// provider does not consider it a usable destination.
    /// </summary>
    Task<string?> ValidateAndNormalizeAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default);
}

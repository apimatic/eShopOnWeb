using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the provider. Returns the provider's canonical
/// (E.164) form of the number, or null when the provider does not consider it a
/// usable destination.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<string?> ValidateAndNormalizeAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

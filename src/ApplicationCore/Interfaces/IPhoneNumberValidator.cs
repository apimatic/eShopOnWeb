using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the provider and returns the provider's canonical form. Used to
/// reject numbers the provider does not consider a usable destination at registration time, rather
/// than at the moment a message fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

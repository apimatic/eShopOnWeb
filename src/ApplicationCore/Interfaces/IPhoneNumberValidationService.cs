using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's canonical form.
/// </summary>
public interface IPhoneNumberValidationService
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawPhoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a provider phone-number lookup.
/// <paramref name="IsValid"/> is the provider's judgement that the number is a usable destination;
/// <paramref name="CanonicalE164"/> is the provider's own canonical E.164 form (null when invalid).
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164);

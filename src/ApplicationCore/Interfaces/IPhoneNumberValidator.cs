using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Result of asking the provider to validate a number. When <see cref="IsValid"/> is true the
/// <see cref="CanonicalE164"/> is the provider's own canonical form of the number, safe to store.
/// </summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> ValidationErrors)
{
    public static PhoneValidationResult Invalid(IReadOnlyList<string> errors) => new(false, null, errors);
    public static PhoneValidationResult Valid(string canonicalE164) => new(true, canonicalE164, System.Array.Empty<string>());
}

/// <summary>
/// Validates a phone number and returns the provider's canonical E.164 form, built to the OpenAPI
/// contract in <c>api-specs/twilio/twilio_lookups_v2</c>. Lookups is served from its own host and is
/// not governed by the messaging <c>Twilio:BaseUrl</c> override.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken);
}

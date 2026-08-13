using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates and canonicalises a phone number with the provider's lookup capability, so an
/// unusable destination is rejected when a number is put on file rather than when a message
/// later fails to go out. This is a distinct provider host from messaging and is not governed
/// by the <c>Twilio:BaseUrl</c> override.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a lookup. <see cref="CanonicalNumber"/> is the provider's own canonical E.164
/// form of the number - what should be stored - and is only populated when <see cref="IsValid"/>.
/// </summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Errors);

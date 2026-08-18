using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider's opinion of a phone number: whether it is a usable destination and, if so,
/// its canonical E.164 form.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Asks the provider (Twilio Lookups v2) whether a number is a usable messaging destination
/// and what its canonical form is, so an unusable number is rejected up front rather than when
/// a message later fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

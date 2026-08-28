using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string number, CancellationToken cancellationToken = default);
}

public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber);

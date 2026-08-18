using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// Validates a candidate mobile number with the provider and returns its canonical form, so a number the
/// provider does not consider usable is rejected at registration rather than when a message later fails.
/// </summary>
public interface IPhoneNumberValidator
{
    /// <param name="rawNumber">The number exactly as the caller typed it (E.164 or national).</param>
    /// <param name="countryCode">Optional ISO-3166 alpha-2 hint used when the input is in national format.</param>
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default);
}

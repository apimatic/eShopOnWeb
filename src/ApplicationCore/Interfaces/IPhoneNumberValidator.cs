using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Asks the provider whether a raw phone number is a usable destination and,
/// if so, what its canonical form is.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, string? countryCode = null, CancellationToken cancellationToken = default);
}

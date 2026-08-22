using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    string? NationalFormat,
    string? CountryCode,
    IReadOnlyList<string> ValidationErrors);

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

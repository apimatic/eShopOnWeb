using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool Valid,
    string? PhoneNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

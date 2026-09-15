using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    string? NationalFormat,
    string? LineType,
    int? LineTypeErrorCode,
    string[] ValidationErrors);

public interface IPhoneNumberLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(bool Valid, string? CanonicalPhoneNumber);

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

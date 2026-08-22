using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public interface ITwilioLookupClient
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

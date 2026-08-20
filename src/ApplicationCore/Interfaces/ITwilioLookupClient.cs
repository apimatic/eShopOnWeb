using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record LookupNumberResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public interface ITwilioLookupClient
{
    Task<LookupNumberResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioLookupResult(bool Valid, string? CanonicalPhoneNumber, IReadOnlyList<string> ValidationErrors);

public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

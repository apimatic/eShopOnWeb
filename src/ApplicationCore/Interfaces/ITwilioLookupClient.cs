using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record TwilioLookupResult(bool IsValid, string? CanonicalPhoneNumber);

public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

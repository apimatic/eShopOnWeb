using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record LookupResult(bool IsValid, string? CanonicalPhoneNumber);

public interface ITwilioLookupClient
{
    Task<LookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

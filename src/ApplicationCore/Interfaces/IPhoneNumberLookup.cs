using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public record PhoneNumberLookupResult(bool Valid, string? CanonicalPhoneNumber, IReadOnlyList<string> ValidationErrors);

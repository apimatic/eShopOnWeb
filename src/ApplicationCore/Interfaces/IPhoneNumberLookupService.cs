using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

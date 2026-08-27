using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class PhoneNumberLookupResult
{
    public bool Succeeded { get; init; }
    public bool Valid { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

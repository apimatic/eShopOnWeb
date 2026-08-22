using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

public sealed class PhoneNumberLookupResult
{
    public bool IsValid { get; init; }
    public string? CanonicalNumber { get; init; }
    public string? NationalFormat { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

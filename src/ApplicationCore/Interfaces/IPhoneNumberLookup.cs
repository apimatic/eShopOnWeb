using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookup
{
    Task<LookedUpPhoneNumber> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

public sealed class LookedUpPhoneNumber
{
    public bool Valid { get; init; }
    public string? PhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public string? CountryCode { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

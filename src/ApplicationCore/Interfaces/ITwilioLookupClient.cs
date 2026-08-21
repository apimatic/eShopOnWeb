using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? PhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public string? CountryCode { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> FetchPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}

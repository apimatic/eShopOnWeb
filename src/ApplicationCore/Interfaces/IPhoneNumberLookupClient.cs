using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(
    bool IsValid,
    string? CanonicalPhoneNumber,
    string? CountryCode,
    IReadOnlyList<string> ValidationErrors);

public interface IPhoneNumberLookupClient
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

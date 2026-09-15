using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    string? NationalFormat,
    string? LineType,
    IReadOnlyList<string> ValidationErrors,
    int? LineTypeErrorCode);

public interface ITwilioLookupClient
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<TwilioPhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class TwilioPhoneLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalNumber { get; init; }
    public string[] ValidationErrors { get; init; } = Array.Empty<string>();
}

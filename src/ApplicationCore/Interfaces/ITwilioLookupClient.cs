using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}

public sealed class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? PhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
    public string? LineType { get; init; }
    public int? LineTypeErrorCode { get; init; }

    public bool IsUsableSmsDestination()
    {
        if (!Valid || string.IsNullOrWhiteSpace(PhoneNumber))
        {
            return false;
        }

        if (LineType is "landline" or "voicemail" or "pager")
        {
            return false;
        }

        return true;
    }
}

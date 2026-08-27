using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for the Twilio Lookups API (lookups.twilio.com), built against the
/// twilio_lookups_v2 OpenAPI document: FetchPhoneNumber on
/// /v2/PhoneNumbers/{PhoneNumber}. Not governed by Twilio:BaseUrl, which
/// applies to the messaging API only.
/// </summary>
public interface ITwilioLookupClient
{
    /// <summary>
    /// Looks up a phone number. Returns the provider's validity verdict and its
    /// canonical E.164 form of the number.
    /// </summary>
    Task<TwilioLookupResult> FetchPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

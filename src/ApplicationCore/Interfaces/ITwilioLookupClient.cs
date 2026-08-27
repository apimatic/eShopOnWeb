using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for Twilio's Lookups API (lookups.twilio.com, v2 PhoneNumbers),
/// built to the OpenAPI contract in api-specs/twilio/twilio_lookups_v2.
/// </summary>
public interface ITwilioLookupClient
{
    Task<TwilioPhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

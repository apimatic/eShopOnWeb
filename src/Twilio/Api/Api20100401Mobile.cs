using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;

namespace Twilio.Api;

public sealed class Api20100401Mobile
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Mobile(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Available mobile phone numbers
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> requesting the AvailablePhoneNumber resources.</param>
    /// <param name="countryCode">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO-3166-1</see> country code of the country from which to read phone numbers.</param>
    /// <param name="areaCode">The area code of the phone numbers to read. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="contains">Matching pattern to identify phone numbers. This pattern can be between 2 and 16 characters long and allows all digits (0-9) and all non-diacritic latin alphabet letters (a-z, A-Z). It accepts four meta-characters: <c>*</c>, <c>%</c>, <c>+</c>, <c>$</c>. The <c>*</c> and <c>%</c> meta-characters can appear multiple times in the pattern. To match wildcards at the beginning or end of the pattern, use <c>*</c> to match any single character or <c>%</c> to match a sequence of characters. If you use the wildcard patterns, it must include at least two non-meta-characters, and wildcards cannot be used between non-meta-characters. To match the beginning of a pattern, start the pattern with <c>+</c>. To match the end of the pattern, append the pattern with <c>$</c>. These meta-characters can't be adjacent to each other.</param>
    /// <param name="smsEnabled">Whether the phone numbers can receive text messages. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="mmsEnabled">Whether the phone numbers can receive MMS messages. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="voiceEnabled">Whether the phone numbers can receive calls. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="excludeAllAddressRequired">Whether to exclude phone numbers that require an <see href="https://www.twilio.com/docs/usage/api/address">Address</see>. Can be: <c>true</c> or <c>false</c> and the default is <c>false</c>.</param>
    /// <param name="excludeLocalAddressRequired">Whether to exclude phone numbers that require a local <see href="https://www.twilio.com/docs/usage/api/address">Address</see>. Can be: <c>true</c> or <c>false</c> and the default is <c>false</c>.</param>
    /// <param name="excludeForeignAddressRequired">Whether to exclude phone numbers that require a foreign <see href="https://www.twilio.com/docs/usage/api/address">Address</see>. Can be: <c>true</c> or <c>false</c> and the default is <c>false</c>.</param>
    /// <param name="beta">Whether to read phone numbers that are new to the Twilio platform. Can be: <c>true</c> or <c>false</c> and the default is <c>true</c>.</param>
    /// <param name="nearNumber">Given a phone number, find a geographically close number within <c>distance</c> miles. Distance defaults to 25 miles. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="nearLatLong">Given a latitude/longitude pair <c>lat,long</c> find geographically close numbers within <c>distance</c> miles. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="distance">The search radius, in miles, for a <c>near_</c> query.  Can be up to <c>500</c> and the default is <c>25</c>. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="inPostalCode">Limit results to a particular postal code. Given a phone number, search within the same postal code as that number. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="inRegion">Limit results to a particular region, state, or province. Given a phone number, search within the same region as that number. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="inRateCenter">Limit results to a specific rate center, or given a phone number search within the same rate center as that number. Requires <c>in_lata</c> to be set as well. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="inLata">Limit results to a specific local access and transport area (<see href="https://en.wikipedia.org/wiki/Local_access_and_transport_area">LATA</see>). Given a phone number, search within the same <see href="https://en.wikipedia.org/wiki/Local_access_and_transport_area">LATA</see> as that number. Applies to only phone numbers in the US and Canada.</param>
    /// <param name="inLocality">Limit results to a particular locality or city. Given a phone number, search within the same Locality as that number.</param>
    /// <param name="faxEnabled">Whether the phone numbers can receive faxes. Can be: <c>true</c> or <c>false</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListAvailablePhoneNumberMobileResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListAvailablePhoneNumberMobileResponse> ListAvailablePhoneNumberMobile(string accountSid,
        string countryCode,
        int? areaCode,
        string? contains,
        bool? smsEnabled,
        bool? mmsEnabled,
        bool? voiceEnabled,
        bool? excludeAllAddressRequired,
        bool? excludeLocalAddressRequired,
        bool? excludeForeignAddressRequired,
        bool? beta,
        string? nearNumber,
        string? nearLatLong,
        int? distance,
        string? inPostalCode,
        string? inRegion,
        string? inRateCenter,
        string? inLata,
        string? inLocality,
        bool? faxEnabled,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/AvailablePhoneNumbers/{CountryCode}/Mobile.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CountryCode", countryCode)],
            [new Param("AreaCode", areaCode),
                new Param("Contains", contains),
                new Param("SmsEnabled", smsEnabled),
                new Param("MmsEnabled", mmsEnabled),
                new Param("VoiceEnabled", voiceEnabled),
                new Param("ExcludeAllAddressRequired", excludeAllAddressRequired),
                new Param("ExcludeLocalAddressRequired", excludeLocalAddressRequired),
                new Param("ExcludeForeignAddressRequired", excludeForeignAddressRequired),
                new Param("Beta", beta),
                new Param("NearNumber", nearNumber),
                new Param("NearLatLong", nearLatLong),
                new Param("Distance", distance),
                new Param("InPostalCode", inPostalCode),
                new Param("InRegion", inRegion),
                new Param("InRateCenter", inRateCenter),
                new Param("InLata", inLata),
                new Param("InLocality", inLocality),
                new Param("FaxEnabled", faxEnabled),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListAvailablePhoneNumberMobileResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

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

public sealed class LookupsV2PhoneNumber
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal LookupsV2PhoneNumber(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Full API documentation: https://www.twilio.com/docs/lookup/v2-api
    /// </summary>
    /// <param name="phoneNumber">The phone number to lookup in E.164 or national format. Default country code is +1 (North America).</param>
    /// <param name="fields">A comma-separated list of fields to return. Possible values are validation, caller_name, sim_swap, call_forwarding, line_status, line_type_intelligence, identity_match, reassigned_number, sms_pumping_risk, phone_number_quality_score, pre_fill.</param>
    /// <param name="countryCode">The <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">country code</see> used if the phone number provided is in national format.</param>
    /// <param name="firstName">User’s first name. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="lastName">User’s last name. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="addressLine1">User’s first address line. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="addressLine2">User’s second address line. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="city">User’s city. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="state">User’s country subdivision, such as state, province, or locality. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="postalCode">User’s postal zip code. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="addressCountryCode">User’s country, up to two characters. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="nationalId">User’s national ID, such as SSN or Passport ID. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="dateOfBirth">User’s date of birth, in YYYYMMDD format. This query parameter is only used (optionally) for identity_match package requests.</param>
    /// <param name="lastVerifiedDate">The date you obtained consent to call or text the end-user of the phone number or a date on which you are reasonably certain that the end-user could still be reached at that number. This query parameter is only used (optionally) for reassigned_number package requests.</param>
    /// <param name="verificationSid">The unique identifier associated with a verification process through verify API. This query parameter is only used (optionally) for pre_fill package requests.</param>
    /// <param name="partnerSubId">The optional partnerSubId parameter to provide context for your sub-accounts, tenantIDs, sender IDs or other segmentation, enhancing the accuracy of the risk analysis.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="LookupResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// The Lookup API allows you to query information on a phone number so that you can make a trusted interaction with your user
    /// </remarks>
    public Task<LookupResponse> FetchPhoneNumber3(string phoneNumber,
        string? fields,
        string? countryCode,
        string? firstName,
        string? lastName,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? state,
        string? postalCode,
        string? addressCountryCode,
        string? nationalId,
        string? dateOfBirth,
        string? lastVerifiedDate,
        string? verificationSid,
        string? partnerSubId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/PhoneNumbers/{PhoneNumber}"),
            [new TemplateParam("PhoneNumber", phoneNumber)],
            [new Param("Fields", fields),
                new Param("CountryCode", countryCode),
                new Param("FirstName", firstName),
                new Param("LastName", lastName),
                new Param("AddressLine1", addressLine1),
                new Param("AddressLine2", addressLine2),
                new Param("City", city),
                new Param("State", state),
                new Param("PostalCode", postalCode),
                new Param("AddressCountryCode", addressCountryCode),
                new Param("NationalId", nationalId),
                new Param("DateOfBirth", dateOfBirth),
                new Param("LastVerifiedDate", lastVerifiedDate),
                new Param("VerificationSid", verificationSid),
                new Param("PartnerSubId", partnerSubId)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<LookupResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

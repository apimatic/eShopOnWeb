using System;
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

public sealed class Api20100401Address
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Address(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// An Address instance resource represents your or your customer's physical location within a country. Around the world, some local authorities require the name and address of the user to be on file with Twilio to purchase and own a phone number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will be responsible for the new Address resource.</param>
    /// <param name="customerName"></param>
    /// <param name="street"></param>
    /// <param name="city"></param>
    /// <param name="region"></param>
    /// <param name="postalCode"></param>
    /// <param name="isoCountry"></param>
    /// <param name="friendlyName"></param>
    /// <param name="emergencyEnabled"></param>
    /// <param name="autoCorrectAddress"></param>
    /// <param name="streetSecondary"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountAddress> CreateAddress(string accountSid,
        string customerName,
        string street,
        string city,
        string region,
        string postalCode,
        string isoCountry,
        string? friendlyName,
        bool? emergencyEnabled,
        bool? autoCorrectAddress,
        string? streetSecondary,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Addresses.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CustomerName", customerName),
                    new Param("Street", street),
                    new Param("City", city),
                    new Param("Region", region),
                    new Param("PostalCode", postalCode),
                    new Param("IsoCountry", isoCountry),
                    new Param("FriendlyName", friendlyName),
                    new Param("EmergencyEnabled", emergencyEnabled),
                    new Param("AutoCorrectAddress", autoCorrectAddress),
                    new Param("StreetSecondary", streetSecondary)]),
            JsonResponse.Create<ApiV2010AccountAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// An Address instance resource represents your or your customer's physical location within a country. Around the world, some local authorities require the name and address of the user to be on file with Twilio to purchase and own a phone number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is responsible for the Address resource to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Address resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteAddress(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Addresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// An Address instance resource represents your or your customer's physical location within a country. Around the world, some local authorities require the name and address of the user to be on file with Twilio to purchase and own a phone number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is responsible for the Address resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Address resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountAddress> FetchAddress(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Addresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// An Address instance resource represents your or your customer's physical location within a country. Around the world, some local authorities require the name and address of the user to be on file with Twilio to purchase and own a phone number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is responsible for the Address resource to read.</param>
    /// <param name="customerName">The <c>customer_name</c> of the Address resources to read.</param>
    /// <param name="friendlyName">The string that identifies the Address resources to read.</param>
    /// <param name="emergencyEnabled">Whether the address can be associated to a number for emergency calling.</param>
    /// <param name="isoCountry">The ISO country code of the Address resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListAddressResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListAddressResponse> ListAddress(string accountSid,
        string? customerName,
        string? friendlyName,
        bool? emergencyEnabled,
        string? isoCountry,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Addresses.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("CustomerName", customerName),
                new Param("FriendlyName", friendlyName),
                new Param("EmergencyEnabled", emergencyEnabled),
                new Param("IsoCountry", isoCountry),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListAddressResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// An Address instance resource represents your or your customer's physical location within a country. Around the world, some local authorities require the name and address of the user to be on file with Twilio to purchase and own a phone number.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is responsible for the Address resource to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Address resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="customerName"></param>
    /// <param name="street"></param>
    /// <param name="city"></param>
    /// <param name="region"></param>
    /// <param name="postalCode"></param>
    /// <param name="emergencyEnabled"></param>
    /// <param name="autoCorrectAddress"></param>
    /// <param name="streetSecondary"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountAddress"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountAddress> UpdateAddress(string accountSid,
        string sid,
        string? friendlyName,
        string? customerName,
        string? street,
        string? city,
        string? region,
        string? postalCode,
        bool? emergencyEnabled,
        bool? autoCorrectAddress,
        string? streetSecondary,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Addresses/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("CustomerName", customerName),
                    new Param("Street", street),
                    new Param("City", city),
                    new Param("Region", region),
                    new Param("PostalCode", postalCode),
                    new Param("EmergencyEnabled", emergencyEnabled),
                    new Param("AutoCorrectAddress", autoCorrectAddress),
                    new Param("StreetSecondary", streetSecondary)]),
            JsonResponse.Create<ApiV2010AccountAddress>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

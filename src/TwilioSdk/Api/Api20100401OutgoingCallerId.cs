using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

public sealed class Api20100401OutgoingCallerId
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401OutgoingCallerId(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete the caller-id specified from the account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the OutgoingCallerId resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the OutgoingCallerId resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete the caller-id specified from the account
    /// </remarks>
    public Task DeleteOutgoingCallerId(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/OutgoingCallerIds/{Sid}.json"),
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
    /// Fetch an outgoing-caller-id belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the OutgoingCallerId resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the OutgoingCallerId resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountOutgoingCallerId"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an outgoing-caller-id belonging to the account used to make the request
    /// </remarks>
    public Task<ApiV2010AccountOutgoingCallerId> FetchOutgoingCallerId(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/OutgoingCallerIds/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountOutgoingCallerId>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of outgoing-caller-ids belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the OutgoingCallerId resources to read.</param>
    /// <param name="phoneNumber">The phone number of the OutgoingCallerId resources to read.</param>
    /// <param name="friendlyName">The string that identifies the OutgoingCallerId resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListOutgoingCallerIdResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of outgoing-caller-ids belonging to the account used to make the request
    /// </remarks>
    public Task<ListOutgoingCallerIdResponse> ListOutgoingCallerId(string accountSid,
        string? phoneNumber,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/OutgoingCallerIds.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("PhoneNumber", phoneNumber),
                new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListOutgoingCallerIdResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Updates the caller-id
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the OutgoingCallerId resources to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the OutgoingCallerId resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountOutgoingCallerId"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the caller-id
    /// </remarks>
    public Task<ApiV2010AccountOutgoingCallerId> UpdateOutgoingCallerId(string accountSid,
        string sid,
        string? friendlyName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/OutgoingCallerIds/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName)]),
            JsonResponse.Create<ApiV2010AccountOutgoingCallerId>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

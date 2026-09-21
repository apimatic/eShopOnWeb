using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class Api20100401ConferenceRecording
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401ConferenceRecording(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a recording from your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference Recording resources to delete.</param>
    /// <param name="conferenceSid">The Conference SID that identifies the conference associated with the recording to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Conference Recording resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a recording from your account
    /// </remarks>
    public Task DeleteConferenceRecording(string accountSid,
        string conferenceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("Sid", sid)],
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
    /// Fetch an instance of a recording for a call
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference Recording resource to fetch.</param>
    /// <param name="conferenceSid">The Conference SID that identifies the conference associated with the recording to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Conference Recording resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConferenceConferenceRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a recording for a call
    /// </remarks>
    public Task<ApiV2010AccountConferenceConferenceRecording> FetchConferenceRecording(string accountSid,
        string conferenceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountConferenceConferenceRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of recordings belonging to the call used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference Recording resources to read.</param>
    /// <param name="conferenceSid">The Conference SID that identifies the conference associated with the recording to read.</param>
    /// <param name="dateCreated">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="dateCreatedQuery">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="dateCreatedQueryQuery">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConferenceRecordingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of recordings belonging to the call used to make the request
    /// </remarks>
    public Task<ListConferenceRecordingResponse> ListConferenceRecording(string accountSid,
        string conferenceSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateCreatedQuery,
        DateTimeOffset? dateCreatedQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Recordings.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("ConferenceSid", conferenceSid)],
            [new Param("DateCreated", dateCreated?.ToDate()),
                new Param("DateCreated<", dateCreatedQuery?.ToDate()),
                new Param("DateCreated>", dateCreatedQueryQuery?.ToDate()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConferenceRecordingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Changes the status of the recording to paused, stopped, or in-progress. Note: To use <c>Twilio.CURRENT</c>, pass it as recording sid.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference Recording resource to update.</param>
    /// <param name="conferenceSid">The Conference SID that identifies the conference associated with the recording to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Conference Recording resource to update. Use <c>Twilio.CURRENT</c> to reference the current active recording.</param>
    /// <param name="status"></param>
    /// <param name="pauseBehavior"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConferenceConferenceRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Changes the status of the recording to paused, stopped, or in-progress. Note: To use <c>Twilio.CURRENT</c>, pass it as recording sid.
    /// </remarks>
    public Task<ApiV2010AccountConferenceConferenceRecording> UpdateConferenceRecording(string accountSid,
        string conferenceSid,
        string sid,
        ConferenceRecordingEnumStatus status,
        string? pauseBehavior,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{ConferenceSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ConferenceSid", conferenceSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status), new Param("PauseBehavior", pauseBehavior)]),
            JsonResponse.Create<ApiV2010AccountConferenceConferenceRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

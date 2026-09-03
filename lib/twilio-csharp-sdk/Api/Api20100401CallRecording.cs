using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Extensions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Errors;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class Api20100401CallRecording
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401CallRecording(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a recording for the call
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="callSid">The SID of the <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> to associate the resource with.</param>
    /// <param name="recordingStatusCallbackEvent"></param>
    /// <param name="recordingStatusCallback"></param>
    /// <param name="recordingStatusCallbackMethod"></param>
    /// <param name="trim"></param>
    /// <param name="recordingChannels"></param>
    /// <param name="recordingTrack"></param>
    /// <param name="recordingConfigurationId"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallCallRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a recording for the call
    /// </remarks>
    public Task<ApiV2010AccountCallCallRecording> CreateCallRecording(string accountSid,
        string callSid,
        IReadOnlyList<string>? recordingStatusCallbackEvent,
        string? recordingStatusCallback,
        RecordingStatusCallbackMethod1? recordingStatusCallbackMethod,
        string? trim,
        string? recordingChannels,
        string? recordingTrack,
        string? recordingConfigurationId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Recordings.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("RecordingStatusCallbackEvent", recordingStatusCallbackEvent),
                    new Param("RecordingStatusCallback", recordingStatusCallback),
                    new Param("RecordingStatusCallbackMethod", recordingStatusCallbackMethod),
                    new Param("Trim", trim),
                    new Param("RecordingChannels", recordingChannels),
                    new Param("RecordingTrack", recordingTrack),
                    new Param("RecordingConfigurationId", recordingConfigurationId)]),
            JsonResponse.Create<ApiV2010AccountCallCallRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a recording from your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resources to delete.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a recording from your account
    /// </remarks>
    public Task DeleteCallRecording(string accountSid,
        string callSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
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
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resource to fetch.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallCallRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a recording for a call
    /// </remarks>
    public Task<ApiV2010AccountCallCallRecording> FetchCallRecording(string accountSid,
        string callSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountCallCallRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of recordings belonging to the call used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resources to read.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resources to read.</param>
    /// <param name="dateCreated">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="dateCreatedQuery">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="dateCreatedQueryQuery">The <c>date_created</c> value, specified as <c>YYYY-MM-DD</c>, of the resources to read. You can also specify inequality: <c>DateCreated&lt;=YYYY-MM-DD</c> will return recordings generated at or before midnight on a given date, and <c>DateCreated&gt;=YYYY-MM-DD</c> returns recordings generated at or after midnight on a date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCallRecordingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of recordings belonging to the call used to make the request
    /// </remarks>
    public Task<ListCallRecordingResponse> ListCallRecording(string accountSid,
        string callSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateCreatedQuery,
        DateTimeOffset? dateCreatedQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Recordings.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [new Param("DateCreated", dateCreated?.ToDate()),
                new Param("DateCreated<", dateCreatedQuery?.ToDate()),
                new Param("DateCreated>", dateCreatedQueryQuery?.ToDate()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCallRecordingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Changes the status of the recording to paused, stopped, or in-progress. Note: Pass <c>Twilio.CURRENT</c> instead of recording sid to reference current active recording.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resource to update.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resource to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording resource to update.</param>
    /// <param name="status"></param>
    /// <param name="pauseBehavior"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallCallRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateCallRecordingError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Changes the status of the recording to paused, stopped, or in-progress. Note: Pass <c>Twilio.CURRENT</c> instead of recording sid to reference current active recording.
    /// </remarks>
    public Task<ApiV2010AccountCallCallRecording> UpdateCallRecording(string accountSid,
        string callSid,
        string sid,
        CallRecordingEnumStatus status,
        string? pauseBehavior,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status), new Param("PauseBehavior", pauseBehavior)]),
            JsonResponse.Create<ApiV2010AccountCallCallRecording>(),
            UpdateCallRecordingErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

using System;
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
using Twilio.Models;

namespace Twilio.Api;

public sealed class Api20100401Recording
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Recording(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a recording from your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a recording from your account
    /// </remarks>
    public Task DeleteRecording(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{Sid}.json"),
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
    /// Fetch an instance of a recording
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Recording resource to fetch.</param>
    /// <param name="includeSoftDeleted">A boolean parameter indicating whether to retrieve soft deleted recordings or not. Recordings metadata are kept after deletion for a retention period of 40 days.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountRecording"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a recording
    /// </remarks>
    public Task<ApiV2010AccountRecording> FetchRecording(string accountSid,
        string sid,
        bool? includeSoftDeleted,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [new Param("IncludeSoftDeleted", includeSoftDeleted)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountRecording>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of recordings belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording resources to read.</param>
    /// <param name="dateCreated">Only include recordings that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read recordings that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read recordings that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read recordings that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQuery">Only include recordings that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read recordings that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read recordings that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read recordings that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQueryQuery">Only include recordings that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read recordings that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read recordings that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read recordings that were created on or after midnight of this date.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the resources to read.</param>
    /// <param name="conferenceSid">The Conference SID that identifies the conference associated with the recording to read.</param>
    /// <param name="includeSoftDeleted">A boolean parameter indicating whether to retrieve soft deleted recordings or not. Recordings metadata are kept after deletion for a retention period of 40 days.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListRecordingResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of recordings belonging to the account used to make the request
    /// </remarks>
    public Task<ListRecordingResponse> ListRecording(string accountSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateCreatedQuery,
        DateTimeOffset? dateCreatedQueryQuery,
        string? callSid,
        string? conferenceSid,
        bool? includeSoftDeleted,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("DateCreated", dateCreated?.ToIso8601()),
                new Param("DateCreated<", dateCreatedQuery?.ToIso8601()),
                new Param("DateCreated>", dateCreatedQueryQuery?.ToIso8601()),
                new Param("CallSid", callSid),
                new Param("ConferenceSid", conferenceSid),
                new Param("IncludeSoftDeleted", includeSoftDeleted),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListRecordingResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

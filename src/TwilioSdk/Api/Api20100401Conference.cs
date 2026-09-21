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

public sealed class Api20100401Conference
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Conference(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch an instance of a conference
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference resource(s) to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Conference resource to fetch</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConference"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a conference
    /// </remarks>
    public Task<ApiV2010AccountConference> FetchConference(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountConference>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of conferences belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference resource(s) to read.</param>
    /// <param name="dateCreated">Only include conferences that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read conferences that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read conferences that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQuery">Only include conferences that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read conferences that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read conferences that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQueryQuery">Only include conferences that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were created on this date. You can also specify an inequality, such as <c>DateCreated&lt;=YYYY-MM-DD</c>, to read conferences that were created on or before midnight of this date, and <c>DateCreated&gt;=YYYY-MM-DD</c> to read conferences that were created on or after midnight of this date.</param>
    /// <param name="dateUpdated">Only include conferences that were last updated on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were last updated on this date. You can also specify an inequality, such as <c>DateUpdated&lt;=YYYY-MM-DD</c>, to read conferences that were last updated on or before midnight of this date, and <c>DateUpdated&gt;=YYYY-MM-DD</c> to read conferences that were last updated on or after midnight of this date.</param>
    /// <param name="dateUpdatedQuery">Only include conferences that were last updated on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were last updated on this date. You can also specify an inequality, such as <c>DateUpdated&lt;=YYYY-MM-DD</c>, to read conferences that were last updated on or before midnight of this date, and <c>DateUpdated&gt;=YYYY-MM-DD</c> to read conferences that were last updated on or after midnight of this date.</param>
    /// <param name="dateUpdatedQueryQuery">Only include conferences that were last updated on this date. Specify a date as <c>YYYY-MM-DD</c> in UTC, for example: <c>2009-07-06</c>, to read only conferences that were last updated on this date. You can also specify an inequality, such as <c>DateUpdated&lt;=YYYY-MM-DD</c>, to read conferences that were last updated on or before midnight of this date, and <c>DateUpdated&gt;=YYYY-MM-DD</c> to read conferences that were last updated on or after midnight of this date.</param>
    /// <param name="friendlyName">The string that identifies the Conference resources to read.</param>
    /// <param name="status">The status of the resources to read. Can be: <c>init</c>, <c>in-progress</c>, or <c>completed</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConferenceResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of conferences belonging to the account used to make the request
    /// </remarks>
    public Task<ListConferenceResponse> ListConference(string accountSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateCreatedQuery,
        DateTimeOffset? dateCreatedQueryQuery,
        DateTimeOffset? dateUpdated,
        DateTimeOffset? dateUpdatedQuery,
        DateTimeOffset? dateUpdatedQueryQuery,
        string? friendlyName,
        ConferenceEnumStatus? status,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("DateCreated", dateCreated?.ToDate()),
                new Param("DateCreated<", dateCreatedQuery?.ToDate()),
                new Param("DateCreated>", dateCreatedQueryQuery?.ToDate()),
                new Param("DateUpdated", dateUpdated?.ToDate()),
                new Param("DateUpdated<", dateUpdatedQuery?.ToDate()),
                new Param("DateUpdated>", dateUpdatedQueryQuery?.ToDate()),
                new Param("FriendlyName", friendlyName),
                new Param("Status", status),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConferenceResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Voice call conferences
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Conference resource(s) to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Conference resource to update</param>
    /// <param name="status"></param>
    /// <param name="announceUrl"></param>
    /// <param name="announceMethod"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountConference"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountConference> UpdateConference(string accountSid,
        string sid,
        ConferenceEnumUpdateStatus? status,
        string? announceUrl,
        AnnounceMethod? announceMethod,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Conferences/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Status", status),
                    new Param("AnnounceUrl", announceUrl),
                    new Param("AnnounceMethod", announceMethod)]),
            JsonResponse.Create<ApiV2010AccountConference>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

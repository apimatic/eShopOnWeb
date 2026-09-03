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

public sealed class Api20100401Media
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Media(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Read a list of Media resources associated with a specific Message resource
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that is associated with the Media resources.</param>
    /// <param name="messageSid">The SID of the Message resource that is associated with the Media resources.</param>
    /// <param name="dateCreated">Only include Media resources that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read Media that were created on this date. You can also specify an inequality, such as <c>StartTime&lt;=YYYY-MM-DD</c>, to read Media that were created on or before midnight of this date, and <c>StartTime&gt;=YYYY-MM-DD</c> to read Media that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQuery">Only include Media resources that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read Media that were created on this date. You can also specify an inequality, such as <c>StartTime&lt;=YYYY-MM-DD</c>, to read Media that were created on or before midnight of this date, and <c>StartTime&gt;=YYYY-MM-DD</c> to read Media that were created on or after midnight of this date.</param>
    /// <param name="dateCreatedQueryQuery">Only include Media resources that were created on this date. Specify a date as <c>YYYY-MM-DD</c> in GMT, for example: <c>2009-07-06</c>, to read Media that were created on this date. You can also specify an inequality, such as <c>StartTime&lt;=YYYY-MM-DD</c>, to read Media that were created on or before midnight of this date, and <c>StartTime&gt;=YYYY-MM-DD</c> to read Media that were created on or after midnight of this date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMediaResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Read a list of Media resources associated with a specific Message resource
    /// </remarks>
    public Task<ListMediaResponse> ListMedia(string accountSid,
        string messageSid,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateCreatedQuery,
        DateTimeOffset? dateCreatedQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages/{MessageSid}/Media.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("MessageSid", messageSid)],
            [new Param("DateCreated", dateCreated?.ToIso8601()),
                new Param("DateCreated<", dateCreatedQuery?.ToIso8601()),
                new Param("DateCreated>", dateCreatedQueryQuery?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMediaResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

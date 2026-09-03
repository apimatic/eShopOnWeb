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

public sealed class Api20100401CallNotification
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401CallNotification(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Error notifications for calls
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call Notification resource to fetch.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the Call Notification resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Call Notification resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallCallNotificationInstance"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ApiV2010AccountCallCallNotificationInstance> FetchCallNotification(string accountSid,
        string callSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Notifications/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountCallCallNotificationInstance>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Error notifications for calls
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Call Notification resources to read.</param>
    /// <param name="callSid">The <see href="https://www.twilio.com/docs/voice/api/call-resource">Call</see> SID of the Call Notification resources to read.</param>
    /// <param name="log">Only read notifications of the specified log level. Can be:  <c>0</c> to read only ERROR notifications or <c>1</c> to read only WARNING notifications. By default, all notifications are read.</param>
    /// <param name="messageDate">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="messageDateQuery">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="messageDateQueryQuery">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCallNotificationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListCallNotificationResponse> ListCallNotification(string accountSid,
        string callSid,
        int? log,
        DateTimeOffset? messageDate,
        DateTimeOffset? messageDateQuery,
        DateTimeOffset? messageDateQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Notifications.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [new Param("Log", log),
                new Param("MessageDate", messageDate?.ToDate()),
                new Param("MessageDate<", messageDateQuery?.ToDate()),
                new Param("MessageDate>", messageDateQueryQuery?.ToDate()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCallNotificationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

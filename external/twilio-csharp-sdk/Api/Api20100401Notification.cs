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

public sealed class Api20100401Notification
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Notification(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch a notification belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Notification resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Notification resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountNotificationInstance"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a notification belonging to the account used to make the request
    /// </remarks>
    public Task<ApiV2010AccountNotificationInstance> FetchNotification(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Notifications/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountNotificationInstance>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of notifications belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Notification resources to read.</param>
    /// <param name="log">Only read notifications of the specified log level. Can be:  <c>0</c> to read only ERROR notifications or <c>1</c> to read only WARNING notifications. By default, all notifications are read.</param>
    /// <param name="messageDate">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="messageDateQuery">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="messageDateQueryQuery">Only show notifications for the specified date, formatted as <c>YYYY-MM-DD</c>. You can also specify an inequality, such as <c>&lt;=YYYY-MM-DD</c> for messages logged at or before midnight on a date, or <c>&gt;=YYYY-MM-DD</c> for messages logged at or after midnight on a date.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListNotificationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of notifications belonging to the account used to make the request
    /// </remarks>
    public Task<ListNotificationResponse> ListNotification(string accountSid,
        int? log,
        DateTimeOffset? messageDate,
        DateTimeOffset? messageDateQuery,
        DateTimeOffset? messageDateQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Notifications.json"),
            [new TemplateParam("AccountSid", accountSid)],
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
            JsonResponse.Create<ListNotificationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

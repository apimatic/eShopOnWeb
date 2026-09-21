using System;
using System.Collections.Generic;
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

public sealed class Api20100401Message
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Message(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Send a message
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> creating the Message resource.</param>
    /// <param name="to"></param>
    /// <param name="statusCallback"></param>
    /// <param name="applicationSid"></param>
    /// <param name="maxPrice"></param>
    /// <param name="provideFeedback"></param>
    /// <param name="attempt"></param>
    /// <param name="validityPeriod"></param>
    /// <param name="forceDelivery"></param>
    /// <param name="contentRetention"></param>
    /// <param name="addressRetention"></param>
    /// <param name="smartEncoded"></param>
    /// <param name="persistentAction"></param>
    /// <param name="trafficType"></param>
    /// <param name="shortenUrls"></param>
    /// <param name="scheduleType"></param>
    /// <param name="sendAt"></param>
    /// <param name="sendAsMms"></param>
    /// <param name="contentVariables"></param>
    /// <param name="riskCheck"></param>
    /// <param name="from"></param>
    /// <param name="fallbackFrom"></param>
    /// <param name="messagingServiceSid"></param>
    /// <param name="body"></param>
    /// <param name="mediaUrl"></param>
    /// <param name="contentSid"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Send a message
    /// </remarks>
    public Task<ApiV2010AccountMessage> CreateMessage(string accountSid,
        string to,
        string? statusCallback,
        string? applicationSid,
        double? maxPrice,
        bool? provideFeedback,
        int? attempt,
        int? validityPeriod,
        bool? forceDelivery,
        MessageEnumContentRetention? contentRetention,
        MessageEnumAddressRetention? addressRetention,
        bool? smartEncoded,
        IReadOnlyList<string>? persistentAction,
        MessageEnumTrafficType? trafficType,
        bool? shortenUrls,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        bool? sendAsMms,
        string? contentVariables,
        MessageEnumRiskCheck? riskCheck,
        string? from,
        string? fallbackFrom,
        string? messagingServiceSid,
        string? body,
        IReadOnlyList<string>? mediaUrl,
        string? contentSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("To", to),
                    new Param("StatusCallback", statusCallback),
                    new Param("ApplicationSid", applicationSid),
                    new Param("MaxPrice", maxPrice),
                    new Param("ProvideFeedback", provideFeedback),
                    new Param("Attempt", attempt),
                    new Param("ValidityPeriod", validityPeriod),
                    new Param("ForceDelivery", forceDelivery),
                    new Param("ContentRetention", contentRetention),
                    new Param("AddressRetention", addressRetention),
                    new Param("SmartEncoded", smartEncoded),
                    new Param("PersistentAction", persistentAction),
                    new Param("TrafficType", trafficType),
                    new Param("ShortenUrls", shortenUrls),
                    new Param("ScheduleType", scheduleType),
                    new Param("SendAt", sendAt),
                    new Param("SendAsMms", sendAsMms),
                    new Param("ContentVariables", contentVariables),
                    new Param("RiskCheck", riskCheck),
                    new Param("From", from),
                    new Param("FallbackFrom", fallbackFrom),
                    new Param("MessagingServiceSid", messagingServiceSid),
                    new Param("Body", body),
                    new Param("MediaUrl", mediaUrl),
                    new Param("ContentSid", contentSid)]),
            JsonResponse.Create<ApiV2010AccountMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Deletes a Message resource from your account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with the Message resource</param>
    /// <param name="sid">The SID of the Message resource you wish to delete</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a Message resource from your account
    /// </remarks>
    public Task DeleteMessage(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json"),
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
    /// Fetch a specific Message
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with the Message resource</param>
    /// <param name="sid">The SID of the Message resource to be fetched</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Message
    /// </remarks>
    public Task<ApiV2010AccountMessage> FetchMessage(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of Message resources associated with a Twilio Account
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> associated with the Message resources.</param>
    /// <param name="to">Filter by recipient. For example: Set this parameter to <c>+15558881111</c> to retrieve a list of Message resources sent to <c>+15558881111</c>.</param>
    /// <param name="from">Filter by sender. For example: Set this parameter to <c>+15552229999</c> to retrieve a list of Message resources sent by <c>+15552229999</c>.</param>
    /// <param name="dateSent">Filter by Message <c>sent_date</c>. Accepts GMT dates in the following formats: <c>YYYY-MM-DD</c> (to find Messages with a specific <c>sent_date</c>), <c>&lt;=YYYY-MM-DD</c> (to find Messages with <c>sent_date</c>s on and before a specific date), and <c>&gt;=YYYY-MM-DD</c> (to find Messages with <c>sent_dates</c> on and after a specific date).</param>
    /// <param name="dateSentQuery">Filter by Message <c>sent_date</c>. Accepts GMT dates in the following formats: <c>YYYY-MM-DD</c> (to find Messages with a specific <c>sent_date</c>), <c>&lt;=YYYY-MM-DD</c> (to find Messages with <c>sent_date</c>s on and before a specific date), and <c>&gt;=YYYY-MM-DD</c> (to find Messages with <c>sent_dates</c> on and after a specific date).</param>
    /// <param name="dateSentQueryQuery">Filter by Message <c>sent_date</c>. Accepts GMT dates in the following formats: <c>YYYY-MM-DD</c> (to find Messages with a specific <c>sent_date</c>), <c>&lt;=YYYY-MM-DD</c> (to find Messages with <c>sent_date</c>s on and before a specific date), and <c>&gt;=YYYY-MM-DD</c> (to find Messages with <c>sent_dates</c> on and after a specific date).</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMessageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Message resources associated with a Twilio Account
    /// </remarks>
    public Task<ListMessageResponse> ListMessage(string accountSid,
        string? to,
        string? from,
        DateTimeOffset? dateSent,
        DateTimeOffset? dateSentQuery,
        DateTimeOffset? dateSentQueryQuery,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("To", to),
                new Param("From", from),
                new Param("DateSent", dateSent?.ToIso8601()),
                new Param("DateSent<", dateSentQuery?.ToIso8601()),
                new Param("DateSent>", dateSentQueryQuery?.ToIso8601()),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMessageResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a Message resource (used to redact Message <c>body</c> text and to cancel not-yet-sent messages)
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Message resources to update.</param>
    /// <param name="sid">The SID of the Message resource to be updated</param>
    /// <param name="body"></param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountMessage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a Message resource (used to redact Message <c>body</c> text and to cancel not-yet-sent messages)
    /// </remarks>
    public Task<ApiV2010AccountMessage> UpdateMessage(string accountSid,
        string sid,
        string? body,
        MessageEnumUpdateStatus? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Body", body), new Param("Status", status)]),
            JsonResponse.Create<ApiV2010AccountMessage>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

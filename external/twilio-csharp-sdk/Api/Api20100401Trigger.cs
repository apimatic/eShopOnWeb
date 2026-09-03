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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class Api20100401Trigger
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Trigger(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new UsageTrigger
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="callbackUrl"></param>
    /// <param name="triggerValue"></param>
    /// <param name="usageCategory"></param>
    /// <param name="callbackMethod"></param>
    /// <param name="friendlyName"></param>
    /// <param name="recurring"></param>
    /// <param name="triggerBy"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountUsageUsageTrigger"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new UsageTrigger
    /// </remarks>
    public Task<ApiV2010AccountUsageUsageTrigger> CreateUsageTrigger(string accountSid,
        string callbackUrl,
        string triggerValue,
        string usageCategory,
        CallbackMethod1? callbackMethod,
        string? friendlyName,
        UsageTriggerEnumRecurring? recurring,
        UsageTriggerEnumTriggerField? triggerBy,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Triggers.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CallbackUrl", callbackUrl),
                    new Param("TriggerValue", triggerValue),
                    new Param("UsageCategory", usageCategory),
                    new Param("CallbackMethod", callbackMethod),
                    new Param("FriendlyName", friendlyName),
                    new Param("Recurring", recurring),
                    new Param("TriggerBy", triggerBy)]),
            JsonResponse.Create<ApiV2010AccountUsageUsageTrigger>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Webhooks that notify you of usage thresholds
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the UsageTrigger resources to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the UsageTrigger resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteUsageTrigger(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Triggers/{Sid}.json"),
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
    /// Fetch and instance of a usage-trigger
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the UsageTrigger resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the UsageTrigger resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountUsageUsageTrigger"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch and instance of a usage-trigger
    /// </remarks>
    public Task<ApiV2010AccountUsageUsageTrigger> FetchUsageTrigger(string accountSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Triggers/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ApiV2010AccountUsageUsageTrigger>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of usage-triggers belonging to the account used to make the request
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the UsageTrigger resources to read.</param>
    /// <param name="recurring">The frequency of recurring UsageTriggers to read. Can be: <c>daily</c>, <c>monthly</c>, or <c>yearly</c> to read recurring UsageTriggers. An empty value or a value of <c>alltime</c> reads non-recurring UsageTriggers.</param>
    /// <param name="triggerBy">The trigger field of the UsageTriggers to read.  Can be: <c>count</c>, <c>usage</c>, or <c>price</c> as described in the <see href="https://www.twilio.com/docs/usage/api/usage-record#usage-count-price">UsageRecords documentation</see>.</param>
    /// <param name="usageCategory">The usage category of the UsageTriggers to read. Must be a supported <see href="https://www.twilio.com/docs/usage/api/usage-record#usage-categories">usage categories</see>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListUsageTriggerResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of usage-triggers belonging to the account used to make the request
    /// </remarks>
    public Task<ListUsageTriggerResponse> ListUsageTrigger(string accountSid,
        UsageTriggerEnumRecurring? recurring,
        UsageTriggerEnumTriggerField? triggerBy,
        string? usageCategory,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Triggers.json"),
            [new TemplateParam("AccountSid", accountSid)],
            [new Param("Recurring", recurring),
                new Param("TriggerBy", triggerBy),
                new Param("UsageCategory", usageCategory),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListUsageTriggerResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update an instance of a usage trigger
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the UsageTrigger resources to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the UsageTrigger resource to update.</param>
    /// <param name="callbackMethod"></param>
    /// <param name="callbackUrl"></param>
    /// <param name="friendlyName"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountUsageUsageTrigger"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an instance of a usage trigger
    /// </remarks>
    public Task<ApiV2010AccountUsageUsageTrigger> UpdateUsageTrigger(string accountSid,
        string sid,
        CallbackMethod1? callbackMethod,
        string? callbackUrl,
        string? friendlyName,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Usage/Triggers/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("CallbackMethod", callbackMethod),
                    new Param("CallbackUrl", callbackUrl),
                    new Param("FriendlyName", friendlyName)]),
            JsonResponse.Create<ApiV2010AccountUsageUsageTrigger>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

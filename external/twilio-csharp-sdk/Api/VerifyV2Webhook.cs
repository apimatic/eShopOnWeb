using System;
using System.Collections.Generic;
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

public sealed class VerifyV2Webhook
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VerifyV2Webhook(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Webhook for the Service
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="friendlyName"></param>
    /// <param name="eventTypes"></param>
    /// <param name="webhookUrl"></param>
    /// <param name="status"></param>
    /// <param name="version"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Webhook for the Service
    /// </remarks>
    public Task<VerifyV2ServiceWebhook> CreateWebhook(string serviceSid,
        string friendlyName,
        IReadOnlyList<string> eventTypes,
        string webhookUrl,
        WebhookEnumStatus? status,
        WebhookEnumVersion? version,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Webhooks"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("EventTypes", eventTypes),
                    new Param("WebhookUrl", webhookUrl),
                    new Param("Status", status),
                    new Param("Version", version)]),
            JsonResponse.Create<VerifyV2ServiceWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Webhook.
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Webhook resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Webhook.
    /// </remarks>
    public Task DeleteWebhook(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Webhooks/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
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
    /// Fetch a specific Webhook.
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Webhook resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VerifyV2ServiceWebhook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Webhook.
    /// </remarks>
    public Task<VerifyV2ServiceWebhook> FetchWebhook(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Webhooks/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VerifyV2ServiceWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Webhooks for a Service.
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListWebhookResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Webhooks for a Service.
    /// </remarks>
    public Task<ListWebhookResponse> ListWebhook(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Webhooks"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListWebhookResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<VerifyV2ServiceWebhook> UpdateWebhook(string serviceSid,
        string sid,
        string? friendlyName,
        IReadOnlyList<string>? eventTypes,
        string? webhookUrl,
        WebhookEnumStatus? status,
        WebhookEnumVersion? version,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Webhooks/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("EventTypes", eventTypes),
                    new Param("WebhookUrl", webhookUrl),
                    new Param("Status", status),
                    new Param("Version", version)]),
            JsonResponse.Create<VerifyV2ServiceWebhook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

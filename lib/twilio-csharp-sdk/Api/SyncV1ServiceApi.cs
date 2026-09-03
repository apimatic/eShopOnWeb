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

namespace Twilio.Api;

public sealed class SyncV1ServiceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1ServiceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Containers for sync objects
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="webhookUrl"></param>
    /// <param name="reachabilityWebhooksEnabled"></param>
    /// <param name="aclEnabled"></param>
    /// <param name="reachabilityDebouncingEnabled"></param>
    /// <param name="reachabilityDebouncingWindow"></param>
    /// <param name="webhooksFromRestEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1Service> CreateService5(string? friendlyName,
        string? webhookUrl,
        bool? reachabilityWebhooksEnabled,
        bool? aclEnabled,
        bool? reachabilityDebouncingEnabled,
        int? reachabilityDebouncingWindow,
        bool? webhooksFromRestEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("WebhookUrl", webhookUrl),
                    new Param("ReachabilityWebhooksEnabled", reachabilityWebhooksEnabled),
                    new Param("AclEnabled", aclEnabled),
                    new Param("ReachabilityDebouncingEnabled", reachabilityDebouncingEnabled),
                    new Param("ReachabilityDebouncingWindow", reachabilityDebouncingWindow),
                    new Param("WebhooksFromRestEnabled", webhooksFromRestEnabled)]),
            JsonResponse.Create<SyncV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Containers for sync objects
    /// </summary>
    /// <param name="sid">The SID of the Service resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteService5(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Containers for sync objects
    /// </summary>
    /// <param name="sid">The SID of the Service resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1Service> FetchService5(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Containers for sync objects
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListServiceResponse4"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListServiceResponse4> ListService5(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListServiceResponse4>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Containers for sync objects
    /// </summary>
    /// <param name="sid">The SID of the Service resource to update.</param>
    /// <param name="webhookUrl"></param>
    /// <param name="friendlyName"></param>
    /// <param name="reachabilityWebhooksEnabled"></param>
    /// <param name="aclEnabled"></param>
    /// <param name="reachabilityDebouncingEnabled"></param>
    /// <param name="reachabilityDebouncingWindow"></param>
    /// <param name="webhooksFromRestEnabled"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1Service"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1Service> UpdateService4(string sid,
        string? webhookUrl,
        string? friendlyName,
        bool? reachabilityWebhooksEnabled,
        bool? aclEnabled,
        bool? reachabilityDebouncingEnabled,
        int? reachabilityDebouncingWindow,
        bool? webhooksFromRestEnabled,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("WebhookUrl", webhookUrl),
                    new Param("FriendlyName", friendlyName),
                    new Param("ReachabilityWebhooksEnabled", reachabilityWebhooksEnabled),
                    new Param("AclEnabled", aclEnabled),
                    new Param("ReachabilityDebouncingEnabled", reachabilityDebouncingEnabled),
                    new Param("ReachabilityDebouncingWindow", reachabilityDebouncingWindow),
                    new Param("WebhooksFromRestEnabled", webhooksFromRestEnabled)]),
            JsonResponse.Create<SyncV1Service>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

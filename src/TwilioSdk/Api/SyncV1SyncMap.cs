using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

public sealed class SyncV1SyncMap
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1SyncMap(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Sync map objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> to create the Sync Map in.</param>
    /// <param name="uniqueName"></param>
    /// <param name="ttl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMap"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMap> CreateSyncMap(string serviceSid,
        string? uniqueName,
        int? ttl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("UniqueName", uniqueName),
                    new Param("Ttl", ttl),
                    new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncMap>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync map objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map resource to delete.</param>
    /// <param name="sid">The SID of the Sync Map resource to delete. Can be the Sync Map's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteSyncMap(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{Sid}"),
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
    /// Sync map objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map resource to fetch.</param>
    /// <param name="sid">The SID of the Sync Map resource to fetch. Can be the Sync Map's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMap"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMap> FetchSyncMap(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1ServiceSyncMap>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync map objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSyncMapResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListSyncMapResponse> ListSyncMap(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSyncMapResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync map objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map resource to update.</param>
    /// <param name="sid">The SID of the Sync Map resource to update. Can be the Sync Map's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="ttl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMap"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMap> UpdateSyncMap(string serviceSid,
        string sid,
        int? ttl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Ttl", ttl), new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncMap>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

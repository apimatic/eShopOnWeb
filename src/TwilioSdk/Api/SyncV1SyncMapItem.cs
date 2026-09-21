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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class SyncV1SyncMapItem
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1SyncMapItem(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Keys in a sync map
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> to create the Map Item in.</param>
    /// <param name="mapSid">The SID of the Sync Map to add the new Map Item to. Can be the Sync Map resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="key"></param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="itemTtl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMapSyncMapItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMapSyncMapItem> CreateSyncMapItem(string serviceSid,
        string mapSid,
        string key,
        object data,
        int? ttl,
        int? itemTtl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{MapSid}/Items"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("MapSid", mapSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Key", key),
                    new Param("Data", data),
                    new Param("Ttl", ttl),
                    new Param("ItemTtl", itemTtl),
                    new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncMapSyncMapItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Keys in a sync map
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map Item resource to delete.</param>
    /// <param name="mapSid">The SID of the Sync Map with the Sync Map Item resource to delete. Can be the Sync Map resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="key">The <c>key</c> value of the Sync Map Item resource to delete.</param>
    /// <param name="ifMatch">If provided, applies this mutation if (and only if) the “revision” field of this [map item] matches the provided value. This matches the semantics of (and is implemented with) the HTTP <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match">If-Match header</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteSyncMapItem(string serviceSid,
        string mapSid,
        string key,
        string? ifMatch,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{MapSid}/Items/{Key}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("MapSid", mapSid),
                new TemplateParam("Key", key)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Keys in a sync map
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map Item resource to fetch.</param>
    /// <param name="mapSid">The SID of the Sync Map with the Sync Map Item resource to fetch. Can be the Sync Map resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="key">The <c>key</c> value of the Sync Map Item resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMapSyncMapItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMapSyncMapItem> FetchSyncMapItem(string serviceSid,
        string mapSid,
        string key,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{MapSid}/Items/{Key}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("MapSid", mapSid),
                new TemplateParam("Key", key)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1ServiceSyncMapSyncMapItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Keys in a sync map
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Map Item resources to read.</param>
    /// <param name="mapSid">The SID of the Sync Map with the Sync Map Item resource to fetch. Can be the Sync Map resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="order">How to order the Map Items returned by their <c>key</c> value. Can be: <c>asc</c> (ascending) or <c>desc</c> (descending) and the default is ascending. Map Items are <see href="https://en.wikipedia.org/wiki/Lexicographical_order">ordered lexicographically</see> by Item key.</param>
    /// <param name="from">The <c>key</c> of the first Sync Map Item resource to read. See also <c>bounds</c>.</param>
    /// <param name="bounds">Whether to include the Map Item referenced by the <c>from</c> parameter. Can be: <c>inclusive</c> to include the Map Item referenced by the <c>from</c> parameter or <c>exclusive</c> to start with the next Map Item. The default value is <c>inclusive</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSyncMapItemResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListSyncMapItemResponse> ListSyncMapItem(string serviceSid,
        string mapSid,
        ChallengeEnumListOrders? order,
        string? from,
        SyncMapItemEnumQueryFromBoundType? bounds,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{MapSid}/Items"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("MapSid", mapSid)],
            [new Param("Order", order),
                new Param("From", from),
                new Param("Bounds", bounds),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSyncMapItemResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Keys in a sync map
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync Map Item resource to update.</param>
    /// <param name="mapSid">The SID of the Sync Map with the Sync Map Item resource to update. Can be the Sync Map resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="key">The <c>key</c> value of the Sync Map Item resource to update.</param>
    /// <param name="ifMatch">If provided, applies this mutation if (and only if) the “revision” field of this [map item] matches the provided value. This matches the semantics of (and is implemented with) the HTTP <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match">If-Match header</see>.</param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="itemTtl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncMapSyncMapItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncMapSyncMapItem> UpdateSyncMapItem(string serviceSid,
        string mapSid,
        string key,
        string? ifMatch,
        object? data,
        int? ttl,
        int? itemTtl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Maps/{MapSid}/Items/{Key}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("MapSid", mapSid),
                new TemplateParam("Key", key)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Data", data),
                    new Param("Ttl", ttl),
                    new Param("ItemTtl", itemTtl),
                    new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncMapSyncMapItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

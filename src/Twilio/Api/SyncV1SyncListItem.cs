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

public sealed class SyncV1SyncListItem
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1SyncListItem(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Items in a sync list
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> to create the new List Item in.</param>
    /// <param name="listSid">The SID of the Sync List to add the new List Item to. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="itemTtl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncListSyncListItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncListSyncListItem> CreateSyncListItem(string serviceSid,
        string listSid,
        object data,
        int? ttl,
        int? itemTtl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Items"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("ListSid", listSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Data", data),
                    new Param("Ttl", ttl),
                    new Param("ItemTtl", itemTtl),
                    new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncListSyncListItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Items in a sync list
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Item resource to delete.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Item resource to delete. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="index">The index of the Sync List Item resource to delete.</param>
    /// <param name="ifMatch">If provided, applies this mutation if (and only if) the “revision” field of this [map item] matches the provided value. This matches the semantics of (and is implemented with) the HTTP <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match">If-Match header</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteSyncListItem(string serviceSid,
        string listSid,
        int index,
        string? ifMatch,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Items/{Index}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Index", index)],
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
    /// Items in a sync list
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Item resource to fetch.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Item resource to fetch. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="index">The index of the Sync List Item resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncListSyncListItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncListSyncListItem> FetchSyncListItem(string serviceSid,
        string listSid,
        int index,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Items/{Index}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Index", index)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1ServiceSyncListSyncListItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Items in a sync list
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the List Item resources to read.</param>
    /// <param name="listSid">The SID of the Sync List with the List Items to read. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="order">How to order the List Items returned by their <c>index</c> value. Can be: <c>asc</c> (ascending) or <c>desc</c> (descending) and the default is ascending.</param>
    /// <param name="from">The <c>index</c> of the first Sync List Item resource to read. See also <c>bounds</c>.</param>
    /// <param name="bounds">Whether to include the List Item referenced by the <c>from</c> parameter. Can be: <c>inclusive</c> to include the List Item referenced by the <c>from</c> parameter or <c>exclusive</c> to start with the next List Item. The default value is <c>inclusive</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSyncListItemResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListSyncListItemResponse> ListSyncListItem(string serviceSid,
        string listSid,
        ChallengeEnumListOrders? order,
        string? from,
        SyncListItemEnumQueryFromBoundType? bounds,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Items"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("ListSid", listSid)],
            [new Param("Order", order),
                new Param("From", from),
                new Param("Bounds", bounds),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSyncListItemResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Items in a sync list
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Sync List Item resource to update.</param>
    /// <param name="listSid">The SID of the Sync List with the Sync List Item resource to update. Can be the Sync List resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="index">The index of the Sync List Item resource to update.</param>
    /// <param name="ifMatch">If provided, applies this mutation if (and only if) the “revision” field of this [map item] matches the provided value. This matches the semantics of (and is implemented with) the HTTP <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/If-Match">If-Match header</see>.</param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="itemTtl"></param>
    /// <param name="collectionTtl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceSyncListSyncListItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceSyncListSyncListItem> UpdateSyncListItem(string serviceSid,
        string listSid,
        int index,
        string? ifMatch,
        object? data,
        int? ttl,
        int? itemTtl,
        int? collectionTtl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Lists/{ListSid}/Items/{Index}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("ListSid", listSid),
                new TemplateParam("Index", index)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Data", data),
                    new Param("Ttl", ttl),
                    new Param("ItemTtl", itemTtl),
                    new Param("CollectionTtl", collectionTtl)]),
            JsonResponse.Create<SyncV1ServiceSyncListSyncListItem>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

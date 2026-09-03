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

public sealed class SyncV1Document
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SyncV1Document(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Sync Document objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> to create the new Document resource in.</param>
    /// <param name="uniqueName"></param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceDocument> CreateDocument(string serviceSid,
        string? uniqueName,
        object? data,
        int? ttl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Documents"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("UniqueName", uniqueName),
                    new Param("Data", data),
                    new Param("Ttl", ttl)]),
            JsonResponse.Create<SyncV1ServiceDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync Document objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Document resource to delete.</param>
    /// <param name="sid">The SID of the Document resource to delete. Can be the Document resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task DeleteDocument(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Documents/{Sid}"),
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
    /// Sync Document objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Document resource to fetch.</param>
    /// <param name="sid">The SID of the Document resource to fetch. Can be the Document resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceDocument> FetchDocument(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Documents/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SyncV1ServiceDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync Document objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Document resources to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 100.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListDocumentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListDocumentResponse> ListDocument(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Documents"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListDocumentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Sync Document objects
    /// </summary>
    /// <param name="serviceSid">The SID of the <see href="https://www.twilio.com/docs/sync/api/service">Sync Service</see> with the Document resource to update.</param>
    /// <param name="sid">The SID of the Document resource to update. Can be the Document resource's <c>sid</c> or its <c>unique_name</c>.</param>
    /// <param name="ifMatch">The If-Match HTTP request header</param>
    /// <param name="data"></param>
    /// <param name="ttl"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SyncV1ServiceDocument"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<SyncV1ServiceDocument> UpdateDocument(string serviceSid,
        string sid,
        string? ifMatch,
        object? data,
        int? ttl,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default12("/v1/Services/{ServiceSid}/Documents/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("If-Match", ifMatch), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Data", data), new Param("Ttl", ttl)]),
            JsonResponse.Create<SyncV1ServiceDocument>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

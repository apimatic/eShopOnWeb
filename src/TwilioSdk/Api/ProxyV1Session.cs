using System;
using System.Collections.Generic;
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

public sealed class ProxyV1Session
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProxyV1Session(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create a new Session
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> resource.</param>
    /// <param name="uniqueName"></param>
    /// <param name="dateExpiry"></param>
    /// <param name="ttl"></param>
    /// <param name="mode"></param>
    /// <param name="status"></param>
    /// <param name="participants"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServiceSession"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create a new Session
    /// </remarks>
    public Task<ProxyV1ServiceSession> CreateSession(string serviceSid,
        string? uniqueName,
        DateTimeOffset? dateExpiry,
        int? ttl,
        SessionEnumMode? mode,
        SessionEnumStatus? status,
        IReadOnlyList<object>? participants,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("UniqueName", uniqueName),
                    new Param("DateExpiry", dateExpiry),
                    new Param("Ttl", ttl),
                    new Param("Mode", mode),
                    new Param("Status", status),
                    new Param("Participants", participants)]),
            JsonResponse.Create<ProxyV1ServiceSession>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a specific Session.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Session resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Session.
    /// </remarks>
    public Task DeleteSession(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{Sid}"),
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
    /// Fetch a specific Session.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Session resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServiceSession"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a specific Session.
    /// </remarks>
    public Task<ProxyV1ServiceSession> FetchSession(string serviceSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProxyV1ServiceSession>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Sessions for the Service. A maximum of 100 records will be returned per page.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to read.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSessionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Sessions for the Service. A maximum of 100 records will be returned per page.
    /// </remarks>
    public Task<ListSessionResponse> ListSession(string serviceSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSessionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update a specific Session.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to update.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Session resource to update.</param>
    /// <param name="dateExpiry"></param>
    /// <param name="ttl"></param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServiceSession"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update a specific Session.
    /// </remarks>
    public Task<ProxyV1ServiceSession> UpdateSession(string serviceSid,
        string sid,
        DateTimeOffset? dateExpiry,
        int? ttl,
        SessionEnumStatus? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("DateExpiry", dateExpiry),
                    new Param("Ttl", ttl),
                    new Param("Status", status)]),
            JsonResponse.Create<ProxyV1ServiceSession>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

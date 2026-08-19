using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Api;

public sealed class Interact
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Interact(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create an interact session
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InteractResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateBrowserSessionError"/> when the server returns an error response.</exception>
    public Task<InteractResponse> CreateBrowserSession(InteractRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/interact"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<InteractResponse>(),
            CreateBrowserSessionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Delete an interact session
    /// </summary>
    /// <param name="sessionId">The interact session ID</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InteractResponse2"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteBrowserSessionError"/> when the server returns an error response.</exception>
    public Task<InteractResponse2> DeleteBrowserSession(string sessionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/interact/{sessionId}"),
            [new TemplateParam("sessionId", sessionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<InteractResponse2>(),
            DeleteBrowserSessionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Execute code in an interact session
    /// </summary>
    /// <param name="sessionId">The interact session ID</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InteractExecuteResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ExecuteBrowserCodeError"/> when the server returns an error response.</exception>
    public Task<InteractExecuteResponse> ExecuteBrowserCode(string sessionId,
        InteractExecuteRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/interact/{sessionId}/execute"),
            [new TemplateParam("sessionId", sessionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<InteractExecuteResponse>(),
            ExecuteBrowserCodeErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// List interact sessions
    /// </summary>
    /// <param name="status">Filter sessions by status</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InteractResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListBrowserSessionsError"/> when the server returns an error response.</exception>
    public Task<InteractResponse1> ListBrowserSessions(Status10? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/interact"),
            [],
            [new Param("status", status)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InteractResponse1>(),
            ListBrowserSessionsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

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

public sealed class StudioV1FlowApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV1FlowApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a specific Flow.
    /// </summary>
    /// <param name="sid">The SID of the Flow resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Flow.
    /// </remarks>
    public Task DeleteFlow(string sid, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v1/Flows/{Sid}"),
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
    /// Retrieve a specific Flow.
    /// </summary>
    /// <param name="sid">The SID of the Flow resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV1Flow"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a specific Flow.
    /// </remarks>
    public Task<StudioV1Flow> FetchFlow(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v1/Flows/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV1Flow>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Flows.
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListFlowResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Flows.
    /// </remarks>
    public Task<ListFlowResponse> ListFlow(long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v1/Flows"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListFlowResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

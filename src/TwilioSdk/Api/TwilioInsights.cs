using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Errors;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

/// <summary>
/// Twilio Insights API.
/// </summary>
public sealed class TwilioInsights
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal TwilioInsights(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Execute a semantic query
    /// </summary>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsQueryResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateQueryResultsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Execute a semantic query against the Conversations domain.
    /// </remarks>
    public Task<InsightsQueryResponse> CreateQueryResults(int? pageSize,
        InsightsQueryRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v3/InsightsDomains/Conversations/Query"),
            [],
            [new Param("pageSize", pageSize)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<InsightsQueryResponse>(),
            CreateQueryResultsErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch Metadata for the Conversations domain
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsMetadataResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchMetadataError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch Metadata for the Conversations domain.
    /// </remarks>
    public Task<InsightsMetadataResponse> FetchMetadata(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v3/InsightsDomains/Conversations/Metadata"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InsightsMetadataResponse>(),
            FetchMetadataErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch semantic query results
    /// </summary>
    /// <param name="pageToken">Pagination token</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsQueryResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchQueryResultsError"/> when the server returns an error response.</exception>
    public Task<InsightsQueryResponse> FetchQueryResults(string pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v3/InsightsDomains/Conversations/Query"),
            [],
            [new Param("pageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InsightsQueryResponse>(),
            FetchQueryResultsErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}

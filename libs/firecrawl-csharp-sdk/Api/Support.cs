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

namespace FirecrawlApi.Api;

public sealed class Support
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Support(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Ask the Firecrawl support agent
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SupportAskResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="AskSupportAgentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Diagnose Firecrawl job, account, and API usage issues with an AI support agent.
    /// </remarks>
    public Task<SupportAskResponse> AskSupportAgent(SupportAskRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/support/ask"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SupportAskResponse>(),
            AskSupportAgentErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Search Firecrawl docs with citations
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SupportDocsSearchResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SearchSupportDocsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Answer Firecrawl documentation questions using the public docs corpus.
    /// </remarks>
    public Task<SupportDocsSearchResponse> SearchSupportDocs(SupportDocsSearchRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/support/docs-search"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SupportDocsSearchResponse>(),
            SearchSupportDocsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

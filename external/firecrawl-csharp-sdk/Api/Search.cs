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

public sealed class Search
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Search(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Search and optionally scrape search results
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SearchResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SearchAndScrapeError"/> when the server returns an error response.</exception>
    public Task<SearchResponse> SearchAndScrape(SearchRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SearchResponse>(),
            SearchAndScrapeErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Submit feedback for a search job
    /// </summary>
    /// <param name="jobId">Search job id returned by /search.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="FeedbackResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SubmitSearchFeedbackError"/> when the server returns an error response.</exception>
    public Task<FeedbackResponse> SubmitSearchFeedback(Guid jobId,
        SearchFeedbackRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/{jobId}/feedback"),
            [new TemplateParam("jobId", jobId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<FeedbackResponse>(),
            SubmitSearchFeedbackErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

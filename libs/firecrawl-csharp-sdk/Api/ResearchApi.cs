using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;
using FirecrawlApi.Models.AnyOf;
using FirecrawlApi.Models.Enums;

namespace FirecrawlApi.Api;

public sealed class ResearchApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ResearchApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Inspect or read a paper
    /// </summary>
    /// <param name="id">Paper reference: a canonical paperId or source-specific primaryId.</param>
    /// <param name="query">When present, returns the top matching full-text passages for this question. Omit it to inspect metadata only.</param>
    /// <param name="k">Passage count for read mode. Only valid when query is present.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SearchResearchPapersResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ResearchGetPaperError"/> when the server returns an error response.</exception>
    public Task<SearchResearchPapersResponse> ResearchGetPaper(string id,
        string? query,
        int? k = 4,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/research/papers/{id}"),
            [new TemplateParam("id", id)],
            [new Param("query", query), new Param("k", k)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SearchResearchPapersResponse>(),
            ResearchGetPaperErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Find related papers
    /// </summary>
    /// <param name="id">Primary seed paper reference.</param>
    /// <param name="intent">Natural-language ranking/filtering intent used for semantic ranking.</param>
    /// <param name="mode">Structural expansion mode.</param>
    /// <param name="rerank">Apply an additional rerank over fused candidates.</param>
    /// <param name="anchor">Additional seed paper reference. Repeat this parameter for multiple anchors.</param>
    /// <param name="k">Maximum number of related papers to return.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ResearchSimilarPapersResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ResearchRelatedPapersError"/> when the server returns an error response.</exception>
    public Task<ResearchSimilarPapersResponse> ResearchRelatedPapers(string id,
        string intent,
        Mode5? mode,
        bool? rerank,
        string? anchor,
        int? k = 40,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/research/papers/{id}/similar"),
            [new TemplateParam("id", id)],
            [new Param("intent", intent),
                new Param("mode", mode),
                new Param("k", k),
                new Param("rerank", rerank),
                new Param("anchor", anchor)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ResearchSimilarPapersResponse>(),
            ResearchRelatedPapersErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Search papers
    /// </summary>
    /// <param name="query">Natural-language paper search query.</param>
    /// <param name="authors">Author substring filter. Repeat or pass a comma-separated value; all filters must match.</param>
    /// <param name="categories">Paper category filter. Repeat or pass a comma-separated value; all filters must match.</param>
    /// <param name="from">Inclusive lower bound on created/updated date.</param>
    /// <param name="to">Inclusive upper bound on created/updated date.</param>
    /// <param name="k">Maximum number of ranked papers to return.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ResearchSearchPapersResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ResearchSearchPapersError"/> when the server returns an error response.</exception>
    public Task<ResearchSearchPapersResponse> ResearchSearchPapers(string query,
        string? authors,
        string? categories,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? k = 40,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/research/papers"),
            [],
            [new Param("query", query),
                new Param("k", k),
                new Param("authors", authors),
                new Param("categories", categories),
                new Param("from", from?.ToDate()),
                new Param("to", to?.ToDate())],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ResearchSearchPapersResponse>(),
            ResearchSearchPapersErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

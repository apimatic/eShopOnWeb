using System;
using System.Collections.Generic;
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

public sealed class Developer
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Developer(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Search the developer index
    /// </summary>
    /// <param name="query">Natural-language question or search phrase.</param>
    /// <param name="types">Result kinds to search. Defaults to all four. Accepts a repeated parameter (<c>types=issue&amp;types=pull_request</c>) or one comma-separated value (<c>types=issue,pull_request</c>).</param>
    /// <param name="repos">Repository slugs to scope the repository half of the index to, such as <c>firecrawl/firecrawl</c>. Applies to the <c>issue</c>, <c>pull_request</c>, and <c>readme</c> types only. Sent together with <c>sources</c>, the two halves are combined rather than intersected, so matching results come back from either. Returns 400 when no repository type is in <c>types</c>, reporting that <c>repos</c> cannot match any requested type and that you should add repository types or drop <c>repos</c>.</param>
    /// <param name="sources">Documentation source ids to scope the documentation half to, at most 20. Applies to the <c>doc</c> type only. Not a fixed enum: ids reflect the documentation sites in the index and the set grows over time, so confirm an id resolves by sending it and reading the <c>sources</c> array on the response. Returns 400 with <c>sources cannot match any requested type; add doc or drop sources</c> when <c>doc</c> is not in <c>types</c>.</param>
    /// <param name="skills">Set to <c>only</c> to limit the search to indexed agent-skill files.</param>
    /// <param name="language">Repository primary language, such as <c>Rust</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results. See <see href="/api-reference/endpoint/developer-search#how-the-repository-filters-scope-a-search">how the repository filters scope a search</see>.</param>
    /// <param name="topic">Repository topic, such as <c>async</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="license">Repository license, such as <c>MIT</c>. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="minStars">Lower bound on repository stars. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="maxStars">Upper bound on repository stars. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="archived">Include or exclude archived repositories. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="fork">Include or exclude forks. Applies to repository results only; sending it with no <c>sources</c> scope returns no <c>doc</c> results.</param>
    /// <param name="k">Number of ranked results to return.</param>
    /// <param name="passages">Matched passages to return per result.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="DeveloperSearchResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeveloperSearchError"/> when the server returns an error response.</exception>
    public Task<DeveloperSearchResponse> DeveloperSearch(string query,
        IReadOnlyList<Types1>? types,
        IReadOnlyList<string>? repos,
        IReadOnlyList<string>? sources,
        Skills? skills,
        string? language,
        string? topic,
        string? license,
        int? minStars,
        int? maxStars,
        bool? archived,
        bool? fork,
        int? k = 10,
        int? passages = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/developer"),
            [],
            [new Param("query", query),
                new Param("k", k),
                new Param("types", types),
                new Param("repos", repos),
                new Param("sources", sources),
                new Param("skills", skills),
                new Param("passages", passages),
                new Param("language", language),
                new Param("topic", topic),
                new Param("license", license),
                new Param("min_stars", minStars),
                new Param("max_stars", maxStars),
                new Param("archived", archived),
                new Param("fork", fork)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<DeveloperSearchResponse>(),
            DeveloperSearchErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Search the developer index
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="DeveloperSearchResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeveloperSearchPostError"/> when the server returns an error response.</exception>
    public Task<DeveloperSearchResponse> DeveloperSearchPost(SearchDeveloperRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/search/developer"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<DeveloperSearchResponse>(),
            DeveloperSearchPostErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

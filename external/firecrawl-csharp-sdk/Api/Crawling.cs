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

public sealed class Crawling
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Crawling(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Cancel a crawl job
    /// </summary>
    /// <param name="id">The ID of the crawl job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelCrawlError"/> when the server returns an error response.</exception>
    public Task<CrawlResponse1> CancelCrawl(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<CrawlResponse1>(),
            CancelCrawlErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Preview crawl parameters generated from natural language prompt
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlParamsPreviewResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CrawlParamsPreviewError"/> when the server returns an error response.</exception>
    public Task<CrawlParamsPreviewResponse> CrawlParamsPreview(CrawlParamsPreviewRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl/params-preview"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CrawlParamsPreviewResponse>(),
            CrawlParamsPreviewErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Crawl multiple URLs based on options
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CrawlUrlsError"/> when the server returns an error response.</exception>
    public Task<CrawlResponse> CrawlUrls(CrawlRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CrawlResponse>(),
            CrawlUrlsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get all active crawls for the authenticated team
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlActiveResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetActiveCrawlsError"/> when the server returns an error response.</exception>
    public Task<CrawlActiveResponse> GetActiveCrawls(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl/active"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CrawlActiveResponse>(),
            GetActiveCrawlsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the errors of a crawl job
    /// </summary>
    /// <param name="id">The ID of the crawl job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlErrorsResponseObj"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetCrawlErrorsError"/> when the server returns an error response.</exception>
    public Task<CrawlErrorsResponseObj> GetCrawlErrors(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl/{id}/errors"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CrawlErrorsResponseObj>(),
            GetCrawlErrorsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the status of a crawl job
    /// </summary>
    /// <param name="id">The ID of the crawl job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlStatusResponseObj"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetCrawlStatusError"/> when the server returns an error response.</exception>
    public Task<CrawlStatusResponseObj> GetCrawlStatus(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/crawl/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CrawlStatusResponseObj>(),
            GetCrawlStatusErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

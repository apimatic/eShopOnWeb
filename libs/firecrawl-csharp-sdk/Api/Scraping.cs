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

public sealed class Scraping
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Scraping(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Cancel a batch scrape job
    /// </summary>
    /// <param name="id">The ID of the batch scrape job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchScrapeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelBatchScrapeError"/> when the server returns an error response.</exception>
    public Task<BatchScrapeResponse> CancelBatchScrape(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/batch/scrape/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<BatchScrapeResponse>(),
            CancelBatchScrapeErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the errors of a batch scrape job
    /// </summary>
    /// <param name="id">The ID of the batch scrape job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CrawlErrorsResponseObj"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetBatchScrapeErrorsError"/> when the server returns an error response.</exception>
    public Task<CrawlErrorsResponseObj> GetBatchScrapeErrors(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/batch/scrape/{id}/errors"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CrawlErrorsResponseObj>(),
            GetBatchScrapeErrorsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the status of a batch scrape job
    /// </summary>
    /// <param name="id">The ID of the batch scrape job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchScrapeStatusResponseObj"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetBatchScrapeStatusError"/> when the server returns an error response.</exception>
    public Task<BatchScrapeStatusResponseObj> GetBatchScrapeStatus(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/batch/scrape/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BatchScrapeStatusResponseObj>(),
            GetBatchScrapeStatusErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the status of a scrape job
    /// </summary>
    /// <param name="jobId">The ID of the job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScrapeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetScrapeStatusError"/> when the server returns an error response.</exception>
    public Task<ScrapeResponse> GetScrapeStatus(Guid jobId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/scrape/{jobId}"),
            [new TemplateParam("jobId", jobId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ScrapeResponse>(),
            GetScrapeStatusErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Interact with the browser session associated with a scrape job
    /// </summary>
    /// <param name="jobId">The scrape job ID</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScrapeInteractResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="InteractWithScrapeBrowserSessionError"/> when the server returns an error response.</exception>
    public Task<ScrapeInteractResponse> InteractWithScrapeBrowserSession(Guid jobId,
        ScrapeInteractRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/scrape/{jobId}/interact"),
            [new TemplateParam("jobId", jobId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ScrapeInteractResponse>(),
            InteractWithScrapeBrowserSessionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Upload and parse a file
    /// </summary>
    /// <param name="file"></param>
    /// <param name="options"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScrapeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ParseFileError"/> when the server returns an error response.</exception>
    public Task<ScrapeResponse> ParseFile(BinaryContent file,
        ParseOptions? options,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/parse"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormRequest.Create([new MultipartParam("file", file),
                    new MultipartParam("options", options, "application/json")]),
            JsonResponse.Create<ScrapeResponse>(),
            ParseFileErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Scrape a single URL and optionally extract information using an LLM
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ScrapeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ScrapeAndExtractFromUrlError"/> when the server returns an error response.</exception>
    public Task<ScrapeResponse> ScrapeAndExtractFromUrl(ScrapeRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/scrape"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ScrapeResponse>(),
            ScrapeAndExtractFromUrlErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Scrape multiple URLs and optionally extract information using an LLM
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BatchScrapeResponseObj"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ScrapeAndExtractFromUrlsError"/> when the server returns an error response.</exception>
    public Task<BatchScrapeResponseObj> ScrapeAndExtractFromUrls(BatchScrapeRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/batch/scrape"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<BatchScrapeResponseObj>(),
            ScrapeAndExtractFromUrlsErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Stop the interactive browser session associated with a scrape job
    /// </summary>
    /// <param name="jobId">The scrape job ID</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SuccessResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="StopInteractiveScrapeBrowserSessionError"/> when the server returns an error response.</exception>
    public Task<SuccessResponse> StopInteractiveScrapeBrowserSession(Guid jobId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/scrape/{jobId}/interact"),
            [new TemplateParam("jobId", jobId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<SuccessResponse>(),
            StopInteractiveScrapeBrowserSessionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

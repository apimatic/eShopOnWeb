using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;

namespace FirecrawlApi.Api;

public sealed class Extraction
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Extraction(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Extract structured data from pages using LLMs
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ExtractResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ExtractDataError"/> when the server returns an error response.</exception>
    public Task<ExtractResponse> ExtractData(ExtractRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/extract"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ExtractResponse>(),
            ExtractDataErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get the status of an extract job
    /// </summary>
    /// <param name="id">The ID of the extract job</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ExtractStatusResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ExtractStatusResponse> GetExtractStatus(Guid id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/extract/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ExtractStatusResponse>(),
            RawErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}

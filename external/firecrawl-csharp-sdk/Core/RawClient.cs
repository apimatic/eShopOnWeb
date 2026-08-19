using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Timeout;
using FirecrawlApi.Core.Authentication;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Logging;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Pagination;
using FirecrawlApi.Core.Pagination.States;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;

namespace FirecrawlApi.Core;

internal sealed class RawClient
{
    private delegate Task<ApiResult<TResponse, TError>> ApiCallHandler<TResponse, TError>(
        ApiRequest request,
        ApiResponse<TResponse, TError> response,
        CancellationToken cancellationToken);

    private readonly HeadersFactory _headerFactory;
    private readonly HttpClient _httpClient;
    private readonly HttpStatusPolicy _statusPolicy;
    private readonly UriFactory _uriFactory;
    private readonly ResiliencePipelineFactory _resiliencePipelineFactory;
    private readonly HttpLogger _httpLogger;

    public RawClient(HttpClient httpClient, UriFactory uriFactory,
        HttpStatusPolicy statusPolicy, HeadersFactory headerFactory, 
        ResiliencePipelineFactory resiliencePipelineFactory, HttpLogger httpLogger)
    {
        _httpClient = httpClient;
        _uriFactory = uriFactory;
        _statusPolicy = statusPolicy;
        _headerFactory = headerFactory;
        _resiliencePipelineFactory = resiliencePipelineFactory;
        _httpLogger = httpLogger;
    }

    public Task<ApiResult<TResponse, TError>> ExecuteResult<TResponse, TError>(
        UrlTemplate urlTemplate,
        IReadOnlyCollection<TemplateParam> templateParameters,
        IReadOnlyCollection<Param> queryParameters,
        IReadOnlyCollection<HeaderParam> headerParameters,
        HttpMethod httpMethod,
        IRequest request,
        IResponse<TResponse> response,
        IErrorResponse<TError> errorResponseDeserializer,
        IReadOnlyList<IAuthScheme> authPolicies,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken) =>
        ExecuteResult(
            new ApiRequest(
                urlTemplate,
                templateParameters,
                queryParameters,
                headerParameters,
                httpMethod,
                request,
                authPolicies),
            ApiResponse.Create(response, errorResponseDeserializer),
            requestOptions,
            cancellationToken);

    public async Task<TResponse> Execute<TResponse, TError>(
        UrlTemplate urlTemplate,
        IReadOnlyCollection<TemplateParam> templateParameters,
        IReadOnlyCollection<Param> queryParameters,
        IReadOnlyCollection<HeaderParam> headerParameters,
        HttpMethod httpMethod,
        IRequest request,
        IResponse<TResponse> response,
        IErrorResponse<TError> errorResponseDeserializer,
        IReadOnlyList<IAuthScheme> authPolicies,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken) =>
        (await ExecuteResult(
            urlTemplate,
            templateParameters,
            queryParameters,
            headerParameters,
            httpMethod,
            request,
            response,
            errorResponseDeserializer,
            authPolicies,
            requestOptions,
            cancellationToken).ConfigureAwait(false)).GetResponseOrThrow();

    public Pageable<TResponse, TItem> ExecutePaged<TResponse, TState, TItem, TError>(
        TState initialState,
        Func<TState, ApiRequest> requestFactory,
        Func<TResponse, IReadOnlyList<TItem>> itemsSelector,
        ApiResponse<TResponse, TError> response,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken)
        where TState : IPageState<TResponse, TState>
    {
        var pages = ExecutePagedResult(initialState, requestFactory, itemsSelector, response,
            requestOptions, cancellationToken);
        return new Pageable<TResponse, TItem>(ThrowOnError(pages.AsPages(cancellationToken), cancellationToken), itemsSelector);

        static async IAsyncEnumerable<TResponse> ThrowOnError(
            IAsyncEnumerable<ApiResult<TResponse, TError>> pages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var result in pages.WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return result.GetResponseOrThrow();
        }
    }

    public Pageable<ApiResult<TResponse, TError>, TItem> ExecutePagedResult<TResponse, TState, TItem, TError>(
        TState initialState,
        Func<TState, ApiRequest> requestFactory,
        Func<TResponse, IReadOnlyList<TItem>> itemsSelector,
        ApiResponse<TResponse, TError> response,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken)
        where TState : IPageState<TResponse, TState>
    {
        var pages = Paginate(initialState, requestFactory, response,
            (req, resp, ct) => ExecuteResult(req, resp, requestOptions, ct), cancellationToken);

        return new Pageable<ApiResult<TResponse, TError>, TItem>(pages, apiResult =>
            apiResult.TryGetResponse(out var page) ? itemsSelector(page) : []);

        static async IAsyncEnumerable<ApiResult<TResponse, TError>> Paginate(
            TState initialState,
            Func<TState, ApiRequest> requestFactory,
            ApiResponse<TResponse, TError> response,
            ApiCallHandler<TResponse, TError> execute,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var state = initialState;

            while (true)
            {
                var result = await execute(requestFactory(state), response, cancellationToken)
                    .ConfigureAwait(false);

                yield return result;

                if (!result.TryGetResponse(out var page))
                    yield break;

                var next = state.Next(page, result.Headers);
                if (next is null)
                    yield break;

                state = next;
            }
        }
    }

    private async Task<ApiResult<TResponse, TError>> ExecuteResult<TResponse, TError>(
        ApiRequest request,
        ApiResponse<TResponse, TError> response,
        RequestOptions? requestOptions,
        CancellationToken cancellationToken)
    {
        var uri = _uriFactory.Create(request.UrlTemplate, request.QueryParameters, request.TemplateParameters);
        var headers = _headerFactory.Create(request.HeaderParameters);
        
        var log = _httpLogger.Begin(request.HttpMethod, uri, requestOptions);
        var pipeline = _resiliencePipelineFactory.Create(request.Request);
        
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResiliencePipelineFactory.LogScopeKey, log);
        context.Properties.Set(ResiliencePipelineFactory.MethodKey, request.HttpMethod);

        // The response is not disposed of here: on success its lifetime is owned by IResponse.Map
        // (buffered responses dispose it immediately, streaming ones hand it to their iterator);
        // the error path below owns disposal explicitly.
        HttpResponseMessage httpResponseMessage;
        try
        {
            httpResponseMessage = await pipeline.ExecuteAsync(async ctx =>
            {
                // Dispose only the content: HttpRequestMessage.Dispose does nothing more.
                var httpRequest = new HttpRequestMessage(request.HttpMethod, uri);
                try
                {
                    httpRequest.Content = request.Request.Get();
                    httpRequest.Headers.AddRange(headers);
                    await request.AuthPolicies.Apply(httpRequest, ctx.CancellationToken).ConfigureAwait(false);

                    await log.RequestSending(httpRequest).ConfigureAwait(false);

                    var httpResponse = await _httpClient
                        .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ctx.CancellationToken)
                        .ConfigureAwait(false);

                    log.ResponseReceived(httpResponse, _statusPolicy.IsSuccess(httpResponse.StatusCode));

                    if (_statusPolicy.IsUnauthorized(httpResponse.StatusCode))
                        request.AuthPolicies.InvalidateRevocable();

                    return httpResponse;
                }
                finally
                {
                    httpRequest.Content?.Dispose();
                }
            }, context).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException ex)
        {
            log.Failed(ex);
            throw new TaskCanceledException(
                "The request was canceled due to the configured RetryOptions.Timeout elapsing.",
                new TimeoutException(ex.Message, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            log.Failed(ex);
            throw;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        // Capture before IResponse.Map runs — buffered responses dispose the message inside Map.
        var statusCode = httpResponseMessage.StatusCode;
        var responseHeaders = httpResponseMessage.Headers;

        if (_statusPolicy.IsSuccess(statusCode))
        {
            var successResponse =
                await response.Response.Map(httpResponseMessage, cancellationToken).ConfigureAwait(false);
            return ApiResult<TResponse, TError>.Success(successResponse, statusCode, responseHeaders);
        }

        using (httpResponseMessage)
        {
            var errorResponse = await response.ErrorResponseDeserializer.Map(httpResponseMessage, cancellationToken)
                .ConfigureAwait(false);

            return ApiResult<TResponse, TError>.Failure(errorResponse, statusCode, responseHeaders);
        }
    }
}

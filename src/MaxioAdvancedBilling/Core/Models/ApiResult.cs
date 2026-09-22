using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using MaxioAdvancedBilling.Core.Exceptions;

namespace MaxioAdvancedBilling.Core.Models;

/// <summary>
///     Represents either a successful response of type <typeparamref name="TResponse" />
///     or an API exception of type <typeparamref name="TError" />.
/// </summary>
public readonly struct ApiResult<TResponse, TError>
{
    private readonly Optional<TResponse> _response;
    private readonly Optional<TError> _error;

    public HttpStatusCode StatusCode { get; }

    public HttpResponseHeaders Headers { get; }

    private ApiResult(Optional<TError> error, HttpStatusCode statusCode, HttpResponseHeaders headers)
    {
        StatusCode = statusCode;
        Headers = headers;
        _error = error;
        _response = default;
    }

    private ApiResult(Optional<TResponse> response, HttpStatusCode statusCode, HttpResponseHeaders headers)
    {
        _response = response;
        StatusCode = statusCode;
        Headers = headers;
        _error = default;
    }

    /// <summary>Create a success result.</summary>
    internal static ApiResult<TResponse, TError> Success(
        TResponse response,
        HttpStatusCode statusCode,
        HttpResponseHeaders headers) =>
        new(Optional<TResponse>.Some(response), statusCode, headers);

    /// <summary>Create a failure result with a typed error.</summary>
    internal static ApiResult<TResponse, TError> Failure(
        TError apiException,
        HttpStatusCode statusCode,
        HttpResponseHeaders headers) =>
        new(Optional<TError>.Some(apiException), statusCode, headers);

    /// <summary>Try to get a response (true when success).</summary>
    public bool TryGetResponse([NotNullWhen(true)] out TResponse? response) =>
        _response.TryGetValue(out response);

    /// <summary>Try to get the API error (true when failure).</summary>
    public bool TryGetError([NotNullWhen(true)] out TError? error) =>
        _error.TryGetValue(out error);

    /// <summary>Pattern-match style handler.</summary>
    public TResult Match<TResult>(Func<TResponse, TResult> onSuccess, Func<TError, TResult> onFailure)
    {
        if (onSuccess is null) throw new ArgumentNullException(nameof(onSuccess));
        if (onFailure is null) throw new ArgumentNullException(nameof(onFailure));

        if (_response.TryGetValue(out var response))
            return onSuccess(response);
        if (_error.TryGetValue(out var exception))
            return onFailure(exception);

        throw new InvalidOperationException("ApiResult is neither success nor failure.");
    }

    /// <summary>Action-based pattern match.</summary>
    public void Match(Action<TResponse> onSuccess, Action<TError> onFailure)
    {
        if (onSuccess is null) throw new ArgumentNullException(nameof(onSuccess));
        if (onFailure is null) throw new ArgumentNullException(nameof(onFailure));

        if (_response.TryGetValue(out var response))
            onSuccess(response);
        else if (_error.TryGetValue(out var exception))
            onFailure(exception);
        else
            throw new InvalidOperationException("ApiResult is neither success nor failure.");
    }

    public void Deconstruct(out bool isSuccess,
        [NotNullWhen(true)] out TResponse? response,
        [NotNullWhen(false)] out TError? error)
    {
        isSuccess = _response.TryGetValue(out response);
        if (isSuccess)
        {
            error = default;
            return;
        }

        _error.TryGetValue(out error);
    }

    public TResponse GetResponseOrThrow()
    {
        if (TryGetResponse(out var success))
            return success;

        if (TryGetError(out var error))
            throw new SdkException<TError> { Error = error };

        throw new InvalidOperationException("ApiResult is neither success nor failure.");
    }

    public override string ToString() => Match(
        r => $"Success({r})",
        error => $"Failure({error})"
    );
}
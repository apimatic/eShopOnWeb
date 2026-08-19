using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class SubmitEndpointFeedbackError : ApiError
{
    private readonly Optional<FeedbackErrorResponse> _feedbackErrorResponseValue;

    private SubmitEndpointFeedbackError(Optional<FeedbackErrorResponse> feedbackErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _feedbackErrorResponseValue = feedbackErrorResponseValue;
    }

    private static SubmitEndpointFeedbackError AsFeedbackErrorResponse(FeedbackErrorResponse value) =>
        new(Optional<FeedbackErrorResponse>.Some(value), default);

    private static SubmitEndpointFeedbackError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetFeedbackErrorResponse(out FeedbackErrorResponse value) =>
        _feedbackErrorResponseValue.TryGetValue(out value);

    internal static Task<SubmitEndpointFeedbackError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 404 or 409 or 500 => FromJson<FeedbackErrorResponse>(response, ct).As(AsFeedbackErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SubmitEndpointFeedbackErrorResponse : IErrorResponse<SubmitEndpointFeedbackError>
{
    public static SubmitEndpointFeedbackErrorResponse Instance { get; } = new();

    private SubmitEndpointFeedbackErrorResponse()
    {
    }

    public Task<SubmitEndpointFeedbackError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SubmitEndpointFeedbackError.Create(response, ct);
}

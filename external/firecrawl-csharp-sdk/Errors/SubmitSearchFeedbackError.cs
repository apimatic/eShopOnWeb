using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class SubmitSearchFeedbackError : ApiError
{
    private readonly Optional<FeedbackErrorResponse> _feedbackErrorResponseValue;

    private SubmitSearchFeedbackError(Optional<FeedbackErrorResponse> feedbackErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _feedbackErrorResponseValue = feedbackErrorResponseValue;
    }

    private static SubmitSearchFeedbackError AsFeedbackErrorResponse(FeedbackErrorResponse value) =>
        new(Optional<FeedbackErrorResponse>.Some(value), default);

    private static SubmitSearchFeedbackError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetFeedbackErrorResponse(out FeedbackErrorResponse value) =>
        _feedbackErrorResponseValue.TryGetValue(out value);

    internal static Task<SubmitSearchFeedbackError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 404 or 409 or 500 => FromJson<FeedbackErrorResponse>(response, ct).As(AsFeedbackErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class SubmitSearchFeedbackErrorResponse : IErrorResponse<SubmitSearchFeedbackError>
{
    public static SubmitSearchFeedbackErrorResponse Instance { get; } = new();

    private SubmitSearchFeedbackErrorResponse()
    {
    }

    public Task<SubmitSearchFeedbackError> Map(HttpResponseMessage response, CancellationToken ct) =>
        SubmitSearchFeedbackError.Create(response, ct);
}

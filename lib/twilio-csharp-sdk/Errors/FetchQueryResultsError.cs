using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Models;
using Twilio.Models;

namespace Twilio.Errors;

public sealed class FetchQueryResultsError : ApiError
{
    private readonly Optional<V3InsightsDomainsConversationsQuery400Error1> _v3InsightsDomainsConversationsQuery400Error1Value;

    private readonly Optional<V3InsightsDomainsConversationsQuery429Error1> _v3InsightsDomainsConversationsQuery429Error1Value;

    private readonly Optional<V3InsightsDomainsConversationsQuery500Error1> _v3InsightsDomainsConversationsQuery500Error1Value;

    private FetchQueryResultsError(Optional<V3InsightsDomainsConversationsQuery400Error1> v3InsightsDomainsConversationsQuery400Error1Value,
        Optional<V3InsightsDomainsConversationsQuery429Error1> v3InsightsDomainsConversationsQuery429Error1Value,
        Optional<V3InsightsDomainsConversationsQuery500Error1> v3InsightsDomainsConversationsQuery500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _v3InsightsDomainsConversationsQuery400Error1Value = v3InsightsDomainsConversationsQuery400Error1Value;
        _v3InsightsDomainsConversationsQuery429Error1Value = v3InsightsDomainsConversationsQuery429Error1Value;
        _v3InsightsDomainsConversationsQuery500Error1Value = v3InsightsDomainsConversationsQuery500Error1Value;
    }

    private static FetchQueryResultsError AsV3InsightsDomainsConversationsQuery400Error1(V3InsightsDomainsConversationsQuery400Error1 value) =>
        new(Optional<V3InsightsDomainsConversationsQuery400Error1>.Some(value), default, default, default);

    private static FetchQueryResultsError AsV3InsightsDomainsConversationsQuery429Error1(V3InsightsDomainsConversationsQuery429Error1 value) =>
        new(default, Optional<V3InsightsDomainsConversationsQuery429Error1>.Some(value), default, default);

    private static FetchQueryResultsError AsV3InsightsDomainsConversationsQuery500Error1(V3InsightsDomainsConversationsQuery500Error1 value) =>
        new(default, default, Optional<V3InsightsDomainsConversationsQuery500Error1>.Some(value), default);

    private static FetchQueryResultsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetV3InsightsDomainsConversationsQuery400Error1(out V3InsightsDomainsConversationsQuery400Error1 value) =>
        _v3InsightsDomainsConversationsQuery400Error1Value.TryGetValue(out value);

    public bool TryGetV3InsightsDomainsConversationsQuery429Error1(out V3InsightsDomainsConversationsQuery429Error1 value) =>
        _v3InsightsDomainsConversationsQuery429Error1Value.TryGetValue(out value);

    public bool TryGetV3InsightsDomainsConversationsQuery500Error1(out V3InsightsDomainsConversationsQuery500Error1 value) =>
        _v3InsightsDomainsConversationsQuery500Error1Value.TryGetValue(out value);

    internal static Task<FetchQueryResultsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<V3InsightsDomainsConversationsQuery400Error1>(response, ct).As(AsV3InsightsDomainsConversationsQuery400Error1),
            429 => FromJson<V3InsightsDomainsConversationsQuery429Error1>(response, ct).As(AsV3InsightsDomainsConversationsQuery429Error1),
            500 => FromJson<V3InsightsDomainsConversationsQuery500Error1>(response, ct).As(AsV3InsightsDomainsConversationsQuery500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class FetchQueryResultsErrorResponse : IErrorResponse<FetchQueryResultsError>
{
    public static FetchQueryResultsErrorResponse Instance { get; } = new();

    private FetchQueryResultsErrorResponse()
    {
    }

    public Task<FetchQueryResultsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        FetchQueryResultsError.Create(response, ct);
}

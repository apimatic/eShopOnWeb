using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Models;
using Twilio.Models;

namespace Twilio.Errors;

public sealed class FetchMetadataError : ApiError
{
    private readonly Optional<V3InsightsDomainsConversationsMetadata400Error1> _v3InsightsDomainsConversationsMetadata400Error1Value;

    private readonly Optional<V3InsightsDomainsConversationsMetadata429Error1> _v3InsightsDomainsConversationsMetadata429Error1Value;

    private readonly Optional<V3InsightsDomainsConversationsMetadata500Error1> _v3InsightsDomainsConversationsMetadata500Error1Value;

    private FetchMetadataError(Optional<V3InsightsDomainsConversationsMetadata400Error1> v3InsightsDomainsConversationsMetadata400Error1Value,
        Optional<V3InsightsDomainsConversationsMetadata429Error1> v3InsightsDomainsConversationsMetadata429Error1Value,
        Optional<V3InsightsDomainsConversationsMetadata500Error1> v3InsightsDomainsConversationsMetadata500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _v3InsightsDomainsConversationsMetadata400Error1Value = v3InsightsDomainsConversationsMetadata400Error1Value;
        _v3InsightsDomainsConversationsMetadata429Error1Value = v3InsightsDomainsConversationsMetadata429Error1Value;
        _v3InsightsDomainsConversationsMetadata500Error1Value = v3InsightsDomainsConversationsMetadata500Error1Value;
    }

    private static FetchMetadataError AsV3InsightsDomainsConversationsMetadata400Error1(V3InsightsDomainsConversationsMetadata400Error1 value) =>
        new(Optional<V3InsightsDomainsConversationsMetadata400Error1>.Some(value), default, default, default);

    private static FetchMetadataError AsV3InsightsDomainsConversationsMetadata429Error1(V3InsightsDomainsConversationsMetadata429Error1 value) =>
        new(default, Optional<V3InsightsDomainsConversationsMetadata429Error1>.Some(value), default, default);

    private static FetchMetadataError AsV3InsightsDomainsConversationsMetadata500Error1(V3InsightsDomainsConversationsMetadata500Error1 value) =>
        new(default, default, Optional<V3InsightsDomainsConversationsMetadata500Error1>.Some(value), default);

    private static FetchMetadataError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetV3InsightsDomainsConversationsMetadata400Error1(out V3InsightsDomainsConversationsMetadata400Error1 value) =>
        _v3InsightsDomainsConversationsMetadata400Error1Value.TryGetValue(out value);

    public bool TryGetV3InsightsDomainsConversationsMetadata429Error1(out V3InsightsDomainsConversationsMetadata429Error1 value) =>
        _v3InsightsDomainsConversationsMetadata429Error1Value.TryGetValue(out value);

    public bool TryGetV3InsightsDomainsConversationsMetadata500Error1(out V3InsightsDomainsConversationsMetadata500Error1 value) =>
        _v3InsightsDomainsConversationsMetadata500Error1Value.TryGetValue(out value);

    internal static Task<FetchMetadataError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<V3InsightsDomainsConversationsMetadata400Error1>(response, ct).As(AsV3InsightsDomainsConversationsMetadata400Error1),
            429 => FromJson<V3InsightsDomainsConversationsMetadata429Error1>(response, ct).As(AsV3InsightsDomainsConversationsMetadata429Error1),
            500 => FromJson<V3InsightsDomainsConversationsMetadata500Error1>(response, ct).As(AsV3InsightsDomainsConversationsMetadata500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class FetchMetadataErrorResponse : IErrorResponse<FetchMetadataError>
{
    public static FetchMetadataErrorResponse Instance { get; } = new();

    private FetchMetadataErrorResponse()
    {
    }

    public Task<FetchMetadataError> Map(HttpResponseMessage response, CancellationToken ct) =>
        FetchMetadataError.Create(response, ct);
}

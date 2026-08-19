using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class MapUrlsError : ApiError
{
    private readonly Optional<Map402Error1> _map402Error1Value;

    private readonly Optional<Map429Error1> _map429Error1Value;

    private readonly Optional<Map500Error1> _map500Error1Value;

    private MapUrlsError(Optional<Map402Error1> map402Error1Value,
        Optional<Map429Error1> map429Error1Value,
        Optional<Map500Error1> map500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _map402Error1Value = map402Error1Value;
        _map429Error1Value = map429Error1Value;
        _map500Error1Value = map500Error1Value;
    }

    private static MapUrlsError AsMap402Error1(Map402Error1 value) =>
        new(Optional<Map402Error1>.Some(value), default, default, default);

    private static MapUrlsError AsMap429Error1(Map429Error1 value) =>
        new(default, Optional<Map429Error1>.Some(value), default, default);

    private static MapUrlsError AsMap500Error1(Map500Error1 value) =>
        new(default, default, Optional<Map500Error1>.Some(value), default);

    private static MapUrlsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetMap402Error1(out Map402Error1 value) => _map402Error1Value.TryGetValue(out value);

    public bool TryGetMap429Error1(out Map429Error1 value) => _map429Error1Value.TryGetValue(out value);

    public bool TryGetMap500Error1(out Map500Error1 value) => _map500Error1Value.TryGetValue(out value);

    internal static Task<MapUrlsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Map402Error1>(response, ct).As(AsMap402Error1),
            429 => FromJson<Map429Error1>(response, ct).As(AsMap429Error1),
            500 => FromJson<Map500Error1>(response, ct).As(AsMap500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class MapUrlsErrorResponse : IErrorResponse<MapUrlsError>
{
    public static MapUrlsErrorResponse Instance { get; } = new();

    private MapUrlsErrorResponse()
    {
    }

    public Task<MapUrlsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        MapUrlsError.Create(response, ct);
}

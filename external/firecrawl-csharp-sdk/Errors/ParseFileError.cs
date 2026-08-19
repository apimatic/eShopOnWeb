using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class ParseFileError : ApiError
{
    private readonly Optional<Parse400Error1> _parse400Error1Value;

    private readonly Optional<Parse402Error1> _parse402Error1Value;

    private readonly Optional<Parse429Error1> _parse429Error1Value;

    private readonly Optional<Parse500Error1> _parse500Error1Value;

    private ParseFileError(Optional<Parse400Error1> parse400Error1Value,
        Optional<Parse402Error1> parse402Error1Value,
        Optional<Parse429Error1> parse429Error1Value,
        Optional<Parse500Error1> parse500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _parse400Error1Value = parse400Error1Value;
        _parse402Error1Value = parse402Error1Value;
        _parse429Error1Value = parse429Error1Value;
        _parse500Error1Value = parse500Error1Value;
    }

    private static ParseFileError AsParse400Error1(Parse400Error1 value) =>
        new(Optional<Parse400Error1>.Some(value), default, default, default, default);

    private static ParseFileError AsParse402Error1(Parse402Error1 value) =>
        new(default, Optional<Parse402Error1>.Some(value), default, default, default);

    private static ParseFileError AsParse429Error1(Parse429Error1 value) =>
        new(default, default, Optional<Parse429Error1>.Some(value), default, default);

    private static ParseFileError AsParse500Error1(Parse500Error1 value) =>
        new(default, default, default, Optional<Parse500Error1>.Some(value), default);

    private static ParseFileError AsFallback(RawError value) =>
        new(default, default, default, default, Optional<RawError>.Some(value));

    public bool TryGetParse400Error1(out Parse400Error1 value) => _parse400Error1Value.TryGetValue(out value);

    public bool TryGetParse402Error1(out Parse402Error1 value) => _parse402Error1Value.TryGetValue(out value);

    public bool TryGetParse429Error1(out Parse429Error1 value) => _parse429Error1Value.TryGetValue(out value);

    public bool TryGetParse500Error1(out Parse500Error1 value) => _parse500Error1Value.TryGetValue(out value);

    internal static Task<ParseFileError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<Parse400Error1>(response, ct).As(AsParse400Error1),
            402 => FromJson<Parse402Error1>(response, ct).As(AsParse402Error1),
            429 => FromJson<Parse429Error1>(response, ct).As(AsParse429Error1),
            500 => FromJson<Parse500Error1>(response, ct).As(AsParse500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ParseFileErrorResponse : IErrorResponse<ParseFileError>
{
    public static ParseFileErrorResponse Instance { get; } = new();

    private ParseFileErrorResponse()
    {
    }

    public Task<ParseFileError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ParseFileError.Create(response, ct);
}

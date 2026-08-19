using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class ListBrowserSessionsError : ApiError
{
    private readonly Optional<Interact402Error1> _interact402Error1Value;

    private ListBrowserSessionsError(Optional<Interact402Error1> interact402Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _interact402Error1Value = interact402Error1Value;
    }

    private static ListBrowserSessionsError AsInteract402Error1(Interact402Error1 value) =>
        new(Optional<Interact402Error1>.Some(value), default);

    private static ListBrowserSessionsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetInteract402Error1(out Interact402Error1 value) =>
        _interact402Error1Value.TryGetValue(out value);

    internal static Task<ListBrowserSessionsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Interact402Error1>(response, ct).As(AsInteract402Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListBrowserSessionsErrorResponse : IErrorResponse<ListBrowserSessionsError>
{
    public static ListBrowserSessionsErrorResponse Instance { get; } = new();

    private ListBrowserSessionsErrorResponse()
    {
    }

    public Task<ListBrowserSessionsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListBrowserSessionsError.Create(response, ct);
}

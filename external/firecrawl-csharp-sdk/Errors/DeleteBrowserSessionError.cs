using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class DeleteBrowserSessionError : ApiError
{
    private readonly Optional<Interact402Error1> _interact402Error1Value;

    private DeleteBrowserSessionError(Optional<Interact402Error1> interact402Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _interact402Error1Value = interact402Error1Value;
    }

    private static DeleteBrowserSessionError AsInteract402Error1(Interact402Error1 value) =>
        new(Optional<Interact402Error1>.Some(value), default);

    private static DeleteBrowserSessionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetInteract402Error1(out Interact402Error1 value) =>
        _interact402Error1Value.TryGetValue(out value);

    internal static Task<DeleteBrowserSessionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Interact402Error1>(response, ct).As(AsInteract402Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeleteBrowserSessionErrorResponse : IErrorResponse<DeleteBrowserSessionError>
{
    public static DeleteBrowserSessionErrorResponse Instance { get; } = new();

    private DeleteBrowserSessionErrorResponse()
    {
    }

    public Task<DeleteBrowserSessionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeleteBrowserSessionError.Create(response, ct);
}

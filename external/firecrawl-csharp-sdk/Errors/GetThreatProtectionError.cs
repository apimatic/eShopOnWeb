using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class GetThreatProtectionError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private GetThreatProtectionError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static GetThreatProtectionError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static GetThreatProtectionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<GetThreatProtectionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            403 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetThreatProtectionErrorResponse : IErrorResponse<GetThreatProtectionError>
{
    public static GetThreatProtectionErrorResponse Instance { get; } = new();

    private GetThreatProtectionErrorResponse()
    {
    }

    public Task<GetThreatProtectionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetThreatProtectionError.Create(response, ct);
}

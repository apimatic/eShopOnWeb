using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Errors;

public sealed class UpdateThreatProtectionError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private UpdateThreatProtectionError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static UpdateThreatProtectionError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static UpdateThreatProtectionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<UpdateThreatProtectionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateThreatProtectionErrorResponse : IErrorResponse<UpdateThreatProtectionError>
{
    public static UpdateThreatProtectionErrorResponse Instance { get; } = new();

    private UpdateThreatProtectionErrorResponse()
    {
    }

    public Task<UpdateThreatProtectionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateThreatProtectionError.Create(response, ct);
}

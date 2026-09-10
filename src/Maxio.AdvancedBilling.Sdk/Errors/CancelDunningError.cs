using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CancelDunningError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private CancelDunningError(Optional<ErrorListResponse1> errorListResponse1Value, Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static CancelDunningError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static CancelDunningError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<CancelDunningError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CancelDunningErrorResponse : IErrorResponse<CancelDunningError>
{
    public static CancelDunningErrorResponse Instance { get; } = new();

    private CancelDunningErrorResponse()
    {
    }

    public Task<CancelDunningError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CancelDunningError.Create(response, ct);
}

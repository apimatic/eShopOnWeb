using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class MigrateSubscriptionProductError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private MigrateSubscriptionProductError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static MigrateSubscriptionProductError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static MigrateSubscriptionProductError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<MigrateSubscriptionProductError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class MigrateSubscriptionProductErrorResponse : IErrorResponse<MigrateSubscriptionProductError>
{
    public static MigrateSubscriptionProductErrorResponse Instance { get; } = new();

    private MigrateSubscriptionProductErrorResponse()
    {
    }

    public Task<MigrateSubscriptionProductError> Map(HttpResponseMessage response, CancellationToken ct) =>
        MigrateSubscriptionProductError.Create(response, ct);
}

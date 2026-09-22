using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class OverrideSubscriptionError : ApiError
{
    private readonly Optional<SingleErrorResponse1> _singleErrorResponse1Value;

    private OverrideSubscriptionError(Optional<SingleErrorResponse1> singleErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _singleErrorResponse1Value = singleErrorResponse1Value;
    }

    private static OverrideSubscriptionError AsSingleErrorResponse1(SingleErrorResponse1 value) =>
        new(Optional<SingleErrorResponse1>.Some(value), default);

    private static OverrideSubscriptionError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSingleErrorResponse1(out SingleErrorResponse1 value) =>
        _singleErrorResponse1Value.TryGetValue(out value);

    internal static Task<OverrideSubscriptionError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<SingleErrorResponse1>(response, ct).As(AsSingleErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class OverrideSubscriptionErrorResponse : IErrorResponse<OverrideSubscriptionError>
{
    public static OverrideSubscriptionErrorResponse Instance { get; } = new();

    private OverrideSubscriptionErrorResponse()
    {
    }

    public Task<OverrideSubscriptionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        OverrideSubscriptionError.Create(response, ct);
}

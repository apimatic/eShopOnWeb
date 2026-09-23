using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ExportSubscriptionsError : ApiError
{
    private readonly Optional<SingleErrorResponse1> _singleErrorResponse1Value;

    private ExportSubscriptionsError(Optional<SingleErrorResponse1> singleErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _singleErrorResponse1Value = singleErrorResponse1Value;
    }

    private static ExportSubscriptionsError AsSingleErrorResponse1(SingleErrorResponse1 value) =>
        new(Optional<SingleErrorResponse1>.Some(value), default);

    private static ExportSubscriptionsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetSingleErrorResponse1(out SingleErrorResponse1 value) =>
        _singleErrorResponse1Value.TryGetValue(out value);

    internal static Task<ExportSubscriptionsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            409 => FromJson<SingleErrorResponse1>(response, ct).As(AsSingleErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ExportSubscriptionsErrorResponse : IErrorResponse<ExportSubscriptionsError>
{
    public static ExportSubscriptionsErrorResponse Instance { get; } = new();

    private ExportSubscriptionsErrorResponse()
    {
    }

    public Task<ExportSubscriptionsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ExportSubscriptionsError.Create(response, ct);
}

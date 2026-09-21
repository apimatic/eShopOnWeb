using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListExportedSubscriptionsError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListExportedSubscriptionsError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListExportedSubscriptionsError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListExportedSubscriptionsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListExportedSubscriptionsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListExportedSubscriptionsErrorResponse : IErrorResponse<ListExportedSubscriptionsError>
{
    public static ListExportedSubscriptionsErrorResponse Instance { get; } = new();

    private ListExportedSubscriptionsErrorResponse()
    {
    }

    public Task<ListExportedSubscriptionsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListExportedSubscriptionsError.Create(response, ct);
}

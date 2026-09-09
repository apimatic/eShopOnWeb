using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListSubscriptionGroupProformaInvoicesError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListSubscriptionGroupProformaInvoicesError(Optional<RawError> noContentValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListSubscriptionGroupProformaInvoicesError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListSubscriptionGroupProformaInvoicesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListSubscriptionGroupProformaInvoicesError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListSubscriptionGroupProformaInvoicesErrorResponse : IErrorResponse<ListSubscriptionGroupProformaInvoicesError>
{
    public static ListSubscriptionGroupProformaInvoicesErrorResponse Instance { get; } = new();

    private ListSubscriptionGroupProformaInvoicesErrorResponse()
    {
    }

    public Task<ListSubscriptionGroupProformaInvoicesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListSubscriptionGroupProformaInvoicesError.Create(response, ct);
}

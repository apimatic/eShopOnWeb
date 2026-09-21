using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListPrepaymentsForSubscriptionGroupError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListPrepaymentsForSubscriptionGroupError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListPrepaymentsForSubscriptionGroupError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListPrepaymentsForSubscriptionGroupError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListPrepaymentsForSubscriptionGroupError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListPrepaymentsForSubscriptionGroupErrorResponse : IErrorResponse<ListPrepaymentsForSubscriptionGroupError>
{
    public static ListPrepaymentsForSubscriptionGroupErrorResponse Instance { get; } = new();

    private ListPrepaymentsForSubscriptionGroupErrorResponse()
    {
    }

    public Task<ListPrepaymentsForSubscriptionGroupError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListPrepaymentsForSubscriptionGroupError.Create(response, ct);
}

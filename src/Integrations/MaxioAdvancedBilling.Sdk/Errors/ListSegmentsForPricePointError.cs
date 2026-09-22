using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListSegmentsForPricePointError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<EventBasedBillingListSegmentsErrors1> _eventBasedBillingListSegmentsErrors1Value;

    private ListSegmentsForPricePointError(Optional<RawError> noContentValue,
        Optional<EventBasedBillingListSegmentsErrors1> eventBasedBillingListSegmentsErrors1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _eventBasedBillingListSegmentsErrors1Value = eventBasedBillingListSegmentsErrors1Value;
    }

    private static ListSegmentsForPricePointError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static ListSegmentsForPricePointError AsEventBasedBillingListSegmentsErrors1(EventBasedBillingListSegmentsErrors1 value) =>
        new(default, Optional<EventBasedBillingListSegmentsErrors1>.Some(value), default);

    private static ListSegmentsForPricePointError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetEventBasedBillingListSegmentsErrors1(out EventBasedBillingListSegmentsErrors1 value) =>
        _eventBasedBillingListSegmentsErrors1Value.TryGetValue(out value);

    internal static Task<ListSegmentsForPricePointError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<EventBasedBillingListSegmentsErrors1>(response, ct).As(AsEventBasedBillingListSegmentsErrors1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListSegmentsForPricePointErrorResponse : IErrorResponse<ListSegmentsForPricePointError>
{
    public static ListSegmentsForPricePointErrorResponse Instance { get; } = new();

    private ListSegmentsForPricePointErrorResponse()
    {
    }

    public Task<ListSegmentsForPricePointError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListSegmentsForPricePointError.Create(response, ct);
}

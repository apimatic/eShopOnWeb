using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class BulkCreateSegmentsError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<EventBasedBillingSegment1> _eventBasedBillingSegment1Value;

    private BulkCreateSegmentsError(Optional<RawError> noContentValue,
        Optional<EventBasedBillingSegment1> eventBasedBillingSegment1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _eventBasedBillingSegment1Value = eventBasedBillingSegment1Value;
    }

    private static BulkCreateSegmentsError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static BulkCreateSegmentsError AsEventBasedBillingSegment1(EventBasedBillingSegment1 value) =>
        new(default, Optional<EventBasedBillingSegment1>.Some(value), default);

    private static BulkCreateSegmentsError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetEventBasedBillingSegment1(out EventBasedBillingSegment1 value) =>
        _eventBasedBillingSegment1Value.TryGetValue(out value);

    internal static Task<BulkCreateSegmentsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<EventBasedBillingSegment1>(response, ct).As(AsEventBasedBillingSegment1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class BulkCreateSegmentsErrorResponse : IErrorResponse<BulkCreateSegmentsError>
{
    public static BulkCreateSegmentsErrorResponse Instance { get; } = new();

    private BulkCreateSegmentsErrorResponse()
    {
    }

    public Task<BulkCreateSegmentsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        BulkCreateSegmentsError.Create(response, ct);
}

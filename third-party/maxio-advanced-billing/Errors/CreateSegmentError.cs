using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateSegmentError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<EventBasedBillingSegmentErrors1> _eventBasedBillingSegmentErrors1Value;

    private CreateSegmentError(Optional<RawError> noContentValue,
        Optional<EventBasedBillingSegmentErrors1> eventBasedBillingSegmentErrors1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _eventBasedBillingSegmentErrors1Value = eventBasedBillingSegmentErrors1Value;
    }

    private static CreateSegmentError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static CreateSegmentError AsEventBasedBillingSegmentErrors1(EventBasedBillingSegmentErrors1 value) =>
        new(default, Optional<EventBasedBillingSegmentErrors1>.Some(value), default);

    private static CreateSegmentError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetEventBasedBillingSegmentErrors1(out EventBasedBillingSegmentErrors1 value) =>
        _eventBasedBillingSegmentErrors1Value.TryGetValue(out value);

    internal static Task<CreateSegmentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<EventBasedBillingSegmentErrors1>(response, ct).As(AsEventBasedBillingSegmentErrors1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateSegmentErrorResponse : IErrorResponse<CreateSegmentError>
{
    public static CreateSegmentErrorResponse Instance { get; } = new();

    private CreateSegmentErrorResponse()
    {
    }

    public Task<CreateSegmentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateSegmentError.Create(response, ct);
}

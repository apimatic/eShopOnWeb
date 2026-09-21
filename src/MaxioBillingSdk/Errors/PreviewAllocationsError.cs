using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class PreviewAllocationsError : ApiError
{
    private readonly Optional<ComponentAllocationError1> _componentAllocationError1Value;

    private PreviewAllocationsError(Optional<ComponentAllocationError1> componentAllocationError1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _componentAllocationError1Value = componentAllocationError1Value;
    }

    private static PreviewAllocationsError AsComponentAllocationError1(ComponentAllocationError1 value) =>
        new(Optional<ComponentAllocationError1>.Some(value), default);

    private static PreviewAllocationsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetComponentAllocationError1(out ComponentAllocationError1 value) =>
        _componentAllocationError1Value.TryGetValue(out value);

    internal static Task<PreviewAllocationsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ComponentAllocationError1>(response, ct).As(AsComponentAllocationError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class PreviewAllocationsErrorResponse : IErrorResponse<PreviewAllocationsError>
{
    public static PreviewAllocationsErrorResponse Instance { get; } = new();

    private PreviewAllocationsErrorResponse()
    {
    }

    public Task<PreviewAllocationsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        PreviewAllocationsError.Create(response, ct);
}

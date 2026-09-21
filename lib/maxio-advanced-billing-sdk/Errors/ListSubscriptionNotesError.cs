using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListSubscriptionNotesError : ApiError
{
    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private ListSubscriptionNotesError(Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static ListSubscriptionNotesError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(Optional<ErrorListResponse1>.Some(value), default);

    private static ListSubscriptionNotesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<ListSubscriptionNotesError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListSubscriptionNotesErrorResponse : IErrorResponse<ListSubscriptionNotesError>
{
    public static ListSubscriptionNotesErrorResponse Instance { get; } = new();

    private ListSubscriptionNotesErrorResponse()
    {
    }

    public Task<ListSubscriptionNotesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListSubscriptionNotesError.Create(response, ct);
}

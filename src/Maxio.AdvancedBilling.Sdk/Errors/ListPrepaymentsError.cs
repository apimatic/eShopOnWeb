using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListPrepaymentsError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListPrepaymentsError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListPrepaymentsError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListPrepaymentsError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListPrepaymentsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListPrepaymentsErrorResponse : IErrorResponse<ListPrepaymentsError>
{
    public static ListPrepaymentsErrorResponse Instance { get; } = new();

    private ListPrepaymentsErrorResponse()
    {
    }

    public Task<ListPrepaymentsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListPrepaymentsError.Create(response, ct);
}

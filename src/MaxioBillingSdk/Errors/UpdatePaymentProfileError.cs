using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdatePaymentProfileError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<ErrorStringMapResponse1> _errorStringMapResponse1Value;

    private UpdatePaymentProfileError(Optional<RawError> noContentValue,
        Optional<ErrorStringMapResponse1> errorStringMapResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _errorStringMapResponse1Value = errorStringMapResponse1Value;
    }

    private static UpdatePaymentProfileError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static UpdatePaymentProfileError AsErrorStringMapResponse1(ErrorStringMapResponse1 value) =>
        new(default, Optional<ErrorStringMapResponse1>.Some(value), default);

    private static UpdatePaymentProfileError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetErrorStringMapResponse1(out ErrorStringMapResponse1 value) =>
        _errorStringMapResponse1Value.TryGetValue(out value);

    internal static Task<UpdatePaymentProfileError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<ErrorStringMapResponse1>(response, ct).As(AsErrorStringMapResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdatePaymentProfileErrorResponse : IErrorResponse<UpdatePaymentProfileError>
{
    public static UpdatePaymentProfileErrorResponse Instance { get; } = new();

    private UpdatePaymentProfileErrorResponse()
    {
    }

    public Task<UpdatePaymentProfileError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdatePaymentProfileError.Create(response, ct);
}

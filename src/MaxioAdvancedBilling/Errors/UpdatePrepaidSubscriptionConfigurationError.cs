using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdatePrepaidSubscriptionConfigurationError : ApiError
{
    private readonly Optional<PrepaidConfigurationErrorResponse> _prepaidConfigurationErrorResponseValue;

    private UpdatePrepaidSubscriptionConfigurationError(Optional<PrepaidConfigurationErrorResponse> prepaidConfigurationErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _prepaidConfigurationErrorResponseValue = prepaidConfigurationErrorResponseValue;
    }

    private static UpdatePrepaidSubscriptionConfigurationError AsPrepaidConfigurationErrorResponse(PrepaidConfigurationErrorResponse value) =>
        new(Optional<PrepaidConfigurationErrorResponse>.Some(value), default);

    private static UpdatePrepaidSubscriptionConfigurationError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetPrepaidConfigurationErrorResponse(out PrepaidConfigurationErrorResponse value) =>
        _prepaidConfigurationErrorResponseValue.TryGetValue(out value);

    internal static Task<UpdatePrepaidSubscriptionConfigurationError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<PrepaidConfigurationErrorResponse>(response, ct).As(AsPrepaidConfigurationErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdatePrepaidSubscriptionConfigurationErrorResponse : IErrorResponse<UpdatePrepaidSubscriptionConfigurationError>
{
    public static UpdatePrepaidSubscriptionConfigurationErrorResponse Instance { get; } = new();

    private UpdatePrepaidSubscriptionConfigurationErrorResponse()
    {
    }

    public Task<UpdatePrepaidSubscriptionConfigurationError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdatePrepaidSubscriptionConfigurationError.Create(response, ct);
}

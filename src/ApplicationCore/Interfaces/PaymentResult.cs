namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum PaymentResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    RequiresApproval,
    ProviderUnavailable
}

/// <summary>
/// The outcome of an orchestration operation. Endpoints translate <see cref="Status"/> into the appropriate
/// HTTP result, so provider failures and validation rejections never surface as an opaque 500 with a leaked
/// message.
/// </summary>
public sealed class PaymentResult<T>
{
    private PaymentResult(PaymentResultStatus status, T? value, string? error)
    {
        Status = status;
        Value = value;
        Error = error;
    }

    public PaymentResultStatus Status { get; }
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess => Status == PaymentResultStatus.Ok;

    public static PaymentResult<T> Ok(T value) => new(PaymentResultStatus.Ok, value, null);
    public static PaymentResult<T> NotFound(string error) => new(PaymentResultStatus.NotFound, default, error);
    public static PaymentResult<T> Invalid(string error) => new(PaymentResultStatus.Invalid, default, error);
    public static PaymentResult<T> Conflict(string error) => new(PaymentResultStatus.Conflict, default, error);
    public static PaymentResult<T> RequiresApproval(string error) => new(PaymentResultStatus.RequiresApproval, default, error);
    public static PaymentResult<T> ProviderUnavailable(string error) => new(PaymentResultStatus.ProviderUnavailable, default, error);
}

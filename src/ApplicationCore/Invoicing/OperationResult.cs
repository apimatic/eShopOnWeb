namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>The outcome kind of an invoicing operation; the API layer maps each onto an HTTP status.</summary>
public enum OperationStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
    Conflict,
    Error
}

/// <summary>
/// A small result carrying an outcome status, a value on success, and a caller-safe message otherwise.
/// Purpose-built so the invoicing endpoints can return precise HTTP semantics (404/403/400/409/500)
/// including the conflict outcomes that a bill's state legitimately produces.
/// </summary>
public sealed class OperationResult<T>
{
    private OperationResult(OperationStatus status, T? value, string? message)
    {
        Status = status;
        Value = value;
        Message = message;
    }

    public OperationStatus Status { get; }
    public T? Value { get; }
    public string? Message { get; }
    public bool IsSuccess => Status == OperationStatus.Ok;

    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value, null);
    public static OperationResult<T> NotFound(string message) => new(OperationStatus.NotFound, default, message);
    public static OperationResult<T> Forbidden(string message) => new(OperationStatus.Forbidden, default, message);
    public static OperationResult<T> Invalid(string message) => new(OperationStatus.Invalid, default, message);
    public static OperationResult<T> Conflict(string message) => new(OperationStatus.Conflict, default, message);
    public static OperationResult<T> Error(string message) => new(OperationStatus.Error, default, message);
}

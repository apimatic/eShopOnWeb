namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>The outcome of an invoicing operation, mapped by the API layer onto an HTTP status.</summary>
public enum OperationStatus
{
    /// <summary>Succeeded.</summary>
    Ok = 0,
    /// <summary>The target does not exist, or the caller may not see it (ownership is not revealed).</summary>
    NotFound = 1,
    /// <summary>The request was well-formed but not valid (for example, an unknown catalog item).</summary>
    Invalid = 2,
    /// <summary>Refused because of the state the bill is in (already issued, withdrawn, or paid).</summary>
    Conflict = 3,
    /// <summary>The invoicing provider could not be reached or returned an unexpected failure.</summary>
    ProviderError = 4
}

/// <summary>
/// A lightweight result carrying an <see cref="OperationStatus"/> and, on success, a value. Used by
/// the invoicing services so the API layer can translate outcomes to precise HTTP status codes
/// without leaking exceptions across the boundary.
/// </summary>
public class OperationResult<T>
{
    private OperationResult(OperationStatus status, T? value, string? error)
    {
        Status = status;
        Value = value;
        Error = error;
    }

    public OperationStatus Status { get; }

    public T? Value { get; }

    public string? Error { get; }

    public bool IsSuccess => Status == OperationStatus.Ok;

    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value, null);

    public static OperationResult<T> NotFound(string error) => new(OperationStatus.NotFound, default, error);

    public static OperationResult<T> Invalid(string error) => new(OperationStatus.Invalid, default, error);

    public static OperationResult<T> Conflict(string error) => new(OperationStatus.Conflict, default, error);

    public static OperationResult<T> ProviderError(string error) => new(OperationStatus.ProviderError, default, error);

    /// <summary>Re-wrap a non-success result of one value type as another (the value is always default).</summary>
    public OperationResult<TOther> To<TOther>() => new(Status, default, Error);
}

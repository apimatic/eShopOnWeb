namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public enum InvoiceOutcome
{
    Success,
    NotFound,
    Conflict,
    BadRequest,
    ProviderError
}

/// <summary>
/// Outcome of an application-service operation. Endpoints translate the outcome to an HTTP
/// status so orchestration logic stays free of web concerns.
/// </summary>
public class OperationResult<T>
{
    private OperationResult(InvoiceOutcome outcome, T? value, string? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
    }

    public InvoiceOutcome Outcome { get; }
    public T? Value { get; }
    public string? Error { get; }

    public bool IsSuccess => Outcome == InvoiceOutcome.Success;

    public static OperationResult<T> Ok(T value) => new(InvoiceOutcome.Success, value, null);
    public static OperationResult<T> NotFound(string message) => new(InvoiceOutcome.NotFound, default, message);
    public static OperationResult<T> Conflict(string message) => new(InvoiceOutcome.Conflict, default, message);
    public static OperationResult<T> BadRequest(string message) => new(InvoiceOutcome.BadRequest, default, message);
    public static OperationResult<T> ProviderError(string message) => new(InvoiceOutcome.ProviderError, default, message);
}

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The expected, non-exceptional outcomes of an invoicing operation. Provider/transport faults are
/// not modelled here — they surface as <see cref="Exceptions.InvoiceProviderException"/> and are
/// mapped centrally at the API boundary.
/// </summary>
public enum ServiceOutcome
{
    Ok,
    NotFound,
    Forbidden,
    Conflict
}

/// <summary>A small result carrier so the API layer can translate domain outcomes to HTTP without exceptions.</summary>
public sealed class ServiceResult<T>
{
    private ServiceResult(ServiceOutcome outcome, T? value, string? error)
    {
        Outcome = outcome;
        Value = value;
        Error = error;
    }

    public ServiceOutcome Outcome { get; }
    public T? Value { get; }
    public string? Error { get; }

    public bool IsOk => Outcome == ServiceOutcome.Ok;

    public static ServiceResult<T> Ok(T value) => new(ServiceOutcome.Ok, value, null);
    public static ServiceResult<T> NotFound(string? error = null) => new(ServiceOutcome.NotFound, default, error);
    public static ServiceResult<T> Forbidden(string? error = null) => new(ServiceOutcome.Forbidden, default, error);
    public static ServiceResult<T> Conflict(string error) => new(ServiceOutcome.Conflict, default, error);
}

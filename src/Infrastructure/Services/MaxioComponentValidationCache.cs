namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Remembers that the configured metered component was checked against the provider, so the UC2
/// precondition ("the handle resolves to a component of metered kind on the family") costs one call
/// per process rather than one per usage report. Registered as a singleton; the billing client
/// itself is transient.
/// </summary>
public class MaxioComponentValidationCache
{
    private readonly object _gate = new();
    private bool _validated;

    public bool IsValidated
    {
        get
        {
            lock (_gate)
            {
                return _validated;
            }
        }
    }

    public void MarkValidated()
    {
        lock (_gate)
        {
            _validated = true;
        }
    }
}

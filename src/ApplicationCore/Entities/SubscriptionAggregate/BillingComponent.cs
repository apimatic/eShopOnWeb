namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The pay-as-you-go metered component (e.g. "api-call") that usage is recorded against.
/// </summary>
public class BillingComponent
{
    public BillingComponent(int id, string handle, ComponentKind kind, string? unitName)
    {
        Id = id;
        Handle = handle;
        Kind = kind;
        UnitName = unitName;
    }

    public int Id { get; }
    public string Handle { get; }
    public ComponentKind Kind { get; }
    public string? UnitName { get; }

    public bool IsMetered => Kind == ComponentKind.Metered;
}

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-side container that holds the recurring plans and the metered component.
/// </summary>
public sealed record ProductFamily
{
    public required int Id { get; init; }

    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}

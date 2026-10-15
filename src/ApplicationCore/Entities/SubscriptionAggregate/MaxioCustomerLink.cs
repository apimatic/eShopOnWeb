namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>Maps an eShopOnWeb user to their Maxio customer. Key: <see cref="UserName"/>.</summary>
public class MaxioCustomerLink : MaxioClaim
{
    public string UserName { get; set; } = string.Empty;
}

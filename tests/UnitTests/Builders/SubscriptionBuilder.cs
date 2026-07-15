using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.UnitTests.Builders;

public class SubscriptionBuilder
{
    public string TestOwnerReference => "buyer@test.com";
    public string TestProductHandle => "eshop-pro";
    public int TestProductId => 7126857;
    public long TestPriceInCents => 29900;

    public Subscription WithState(int id, SubscriptionState state, string? ownerReference = null, string? productHandle = null) =>
        new(id, ownerReference ?? TestOwnerReference, productHandle ?? TestProductHandle, TestProductId, TestPriceInCents, state, null, null, null);
}

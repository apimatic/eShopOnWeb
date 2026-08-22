using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.ContactTests;

public class ContactNumberLifecycle
{
    [Fact]
    public void NewContactIsActive()
    {
        var contact = new ContactNumber("buyer-1", "+14155552671");

        Assert.True(contact.IsActive);
        Assert.Equal("+14155552671", contact.PhoneNumber);
        Assert.Equal("buyer-1", contact.BuyerId);
    }

    [Fact]
    public void DeactivatePreventsReuseUntilReactivated()
    {
        var contact = new ContactNumber("buyer-1", "+14155552671");

        contact.Deactivate();
        Assert.False(contact.IsActive);

        contact.Reactivate();
        Assert.True(contact.IsActive);
    }
}

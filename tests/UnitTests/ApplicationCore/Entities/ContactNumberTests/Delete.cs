using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.ContactNumberTests;

public class Delete
{
    [Fact]
    public void RemovesTheDestinationAndIsIdempotent()
    {
        var contact = new ContactNumber("shopper@example.com", "+14165550123", DateTimeOffset.UtcNow);
        var deletedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        contact.Delete(deletedAt);
        contact.Delete(deletedAt.AddMinutes(1));

        Assert.False(contact.IsActive);
        Assert.Empty(contact.Number);
        Assert.Equal(deletedAt, contact.DeletedAt);
    }
}

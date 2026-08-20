using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ContactNumberServiceTests;

public class RegisterContactNumber
{
    [Fact]
    public async Task StoresCanonicalFormFromProvider()
    {
        var repo = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<IOrderMessagingGateway>();
        gateway.LookupAsync(Arg.Any<string>(), default)
            .Returns(new PhoneNumberLookup("+15555550100", true, System.Array.Empty<string>(), "mobile"));
        repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByBuyerAndCanonicalSpecification>(), default)
            .Returns((ShopperContactNumber?)null);
        repo.AddAsync(Arg.Any<ShopperContactNumber>(), default)
            .Returns(ci => ci.Arg<ShopperContactNumber>());

        var service = new ContactNumberService(repo, gateway);
        var created = await service.RegisterAsync("buyer@example.com", "555-555-0100", default);

        Assert.Equal("+15555550100", created.CanonicalNumber);
        Assert.Equal("buyer@example.com", created.BuyerId);
    }

    [Fact]
    public async Task RejectsWhenProviderSaysInvalid()
    {
        var repo = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<IOrderMessagingGateway>();
        gateway.LookupAsync(Arg.Any<string>(), default)
            .Returns(new PhoneNumberLookup("+15555550100", false, new[] { "TOO_SHORT" }, null));

        var service = new ContactNumberService(repo, gateway);

        await Assert.ThrowsAsync<InvalidContactNumberException>(() =>
            service.RegisterAsync("buyer@example.com", "123", default));
        await repo.DidNotReceive().AddAsync(Arg.Any<ShopperContactNumber>(), default);
    }
}

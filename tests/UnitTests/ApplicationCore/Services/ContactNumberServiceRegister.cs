using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceRegister
{
    [Fact]
    public async Task RejectsUnusableDestination()
    {
        var repo = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<ISmsNotificationGateway>();
        var logger = Substitute.For<IAppLogger<ContactNumberService>>();
        gateway.LookupNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(false, null, "This number is not a usable destination."));

        var service = new ContactNumberService(repo, gateway, logger);
        var result = await service.RegisterAsync("buyer", "+10000000000", CancellationToken.None);

        Assert.False(result.IsSuccess);
        await repo.DidNotReceive().AddAsync(Arg.Any<ShopperContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresCanonicalNumberFromProvider()
    {
        var repo = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<ISmsNotificationGateway>();
        var logger = Substitute.For<IAppLogger<ContactNumberService>>();
        gateway.LookupNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(true, "+15551234567", null));
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ShopperContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ShopperContactNumber?)null);
        repo.AddAsync(Arg.Any<ShopperContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ShopperContactNumber>());

        var service = new ContactNumberService(repo, gateway, logger);
        var result = await service.RegisterAsync("buyer", "555-123-4567", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("+15551234567", result.Value.CanonicalNumber);
        await repo.Received(1).AddAsync(Arg.Is<ShopperContactNumber>(c => c.CanonicalNumber == "+15551234567"), Arg.Any<CancellationToken>());
    }
}

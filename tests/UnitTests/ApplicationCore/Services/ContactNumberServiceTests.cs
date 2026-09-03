using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    [Fact]
    public async Task RegisterRejectsUnusableDestination()
    {
        var repository = Substitute.For<IRepository<ShopperContactNumber>>();
        var lookup = Substitute.For<IPhoneNumberLookupService>();
        var logger = Substitute.For<IAppLogger<ContactNumberService>>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult { IsUsable = false });

        var service = new ContactNumberService(repository, lookup, logger);

        await Assert.ThrowsAsync<ContactNumberRejectedException>(() =>
            service.RegisterAsync("demouser@microsoft.com", "+10000000000", CancellationToken.None));
        await repository.DidNotReceive().AddAsync(Arg.Any<ShopperContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterStoresCanonicalForm()
    {
        var repository = Substitute.For<IRepository<ShopperContactNumber>>();
        var lookup = Substitute.For<IPhoneNumberLookupService>();
        var logger = Substitute.For<IAppLogger<ContactNumberService>>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult { IsUsable = true, CanonicalNumber = "+15551234567" });

        ShopperContactNumber? stored = null;
        repository.AddAsync(Arg.Do<ShopperContactNumber>(c => stored = c), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(stored!));

        var service = new ContactNumberService(repository, lookup, logger);
        await service.RegisterAsync("demouser@microsoft.com", "5551234567", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal("+15551234567", stored!.CanonicalNumber);
        Assert.Equal("demouser@microsoft.com", stored.BuyerId);
    }
}

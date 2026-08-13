using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private const string BuyerId = "demouser@microsoft.com";

    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsNotificationGateway _gateway = Substitute.For<ISmsNotificationGateway>();

    private ContactNumberService CreateService()
    {
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<ContactNumber>());
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        return new ContactNumberService(_repo, _gateway);
    }

    [Fact]
    public async Task Register_WhenProviderRejectsNumber_IsRejectedAndNotStored()
    {
        var service = CreateService();
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PhoneValidationResult.NotUsable("bad number"));

        var result = await service.RegisterAsync(BuyerId, "garbage");

        Assert.False(result.Success);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenUsable_StoresProviderCanonicalForm()
    {
        var service = CreateService();
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PhoneValidationResult.Usable("+15551234567"));

        var result = await service.RegisterAsync(BuyerId, "(555) 123-4567");

        Assert.True(result.Success);
        Assert.Equal("+15551234567", result.ContactNumber!.PhoneNumber);
        await _repo.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.PhoneNumber == "+15551234567" && c.BuyerId == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WhenNotTheBuyersNumber_ReturnsFalse()
    {
        var service = CreateService();
        // Scoped spec returns null for another shopper's / a missing number.
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var deleted = await service.DeleteAsync(BuyerId, 123);

        Assert.False(deleted);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WhenOwned_RemovesIt()
    {
        var service = CreateService();
        var number = new ContactNumber(BuyerId, "+15551234567");
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(number);

        var deleted = await service.DeleteAsync(BuyerId, 1);

        Assert.True(deleted);
        await _repo.Received(1).DeleteAsync(number, Arg.Any<CancellationToken>());
    }
}

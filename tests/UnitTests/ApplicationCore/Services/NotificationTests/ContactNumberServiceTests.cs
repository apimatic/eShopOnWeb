using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class ContactNumberServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateSut() => new(_repo, _gateway, _logger);

    [Fact]
    public async Task Register_UnusableDestination_IsRejectedAndNothingStored()
    {
        _gateway.LookupAsync("garbage", Arg.Any<CancellationToken>()).Returns(new PhoneLookupResult(false, null));
        var sut = CreateSut();

        var result = await sut.RegisterAsync(BuyerId, "garbage");

        Assert.Null(result);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_StoresProviderCanonicalForm_NotRawInput()
    {
        _gateway.LookupAsync("(555) 000-0001", Arg.Any<CancellationToken>())
                .Returns(new PhoneLookupResult(true, "+15550000001"));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
             .Returns(new List<ContactNumber>());
        ContactNumber? stored = null;
        await _repo.AddAsync(Arg.Do<ContactNumber>(c => stored = c), Arg.Any<CancellationToken>());
        var sut = CreateSut();

        var result = await sut.RegisterAsync(BuyerId, "(555) 000-0001");

        Assert.NotNull(result);
        Assert.Equal("+15550000001", result!.PhoneNumber); // canonical, not the raw input
        Assert.NotNull(stored);
        Assert.Equal("+15550000001", stored!.PhoneNumber);
    }

    [Fact]
    public async Task Register_IsIdempotentForSameCanonicalNumber()
    {
        _gateway.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new PhoneLookupResult(true, "+15550000001"));
        var already = new ContactNumber(BuyerId, "+15550000001");
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
             .Returns(new List<ContactNumber> { already });
        var sut = CreateSut();

        var result = await sut.RegisterAsync(BuyerId, "+1 555 000 0001");

        Assert.Same(already, result);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_NumberNotOwnedByCaller_ReturnsFalseAndDeletesNothing()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
             .Returns((ContactNumber?)null); // scoped spec finds nothing for this buyer
        var sut = CreateSut();

        var removed = await sut.RemoveAsync(BuyerId, 42);

        Assert.False(removed);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_OwnNumber_DeletesAndReturnsTrue()
    {
        var mine = new ContactNumber(BuyerId, "+15550000001");
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
             .Returns(mine);
        var sut = CreateSut();

        var removed = await sut.RemoveAsync(BuyerId, 1);

        Assert.True(removed);
        await _repo.Received(1).DeleteAsync(mine, Arg.Any<CancellationToken>());
    }
}

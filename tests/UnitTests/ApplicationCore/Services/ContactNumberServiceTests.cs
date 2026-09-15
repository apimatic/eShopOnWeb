using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();

    private ContactNumberService Service() => new(_repo, _gateway);

    [Fact]
    public async Task RejectsUnusableNumberAndDoesNotStore()
    {
        _gateway.ValidateNumberAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null));

        var result = await Service().RegisterAsync(BuyerId, "garbage");

        Assert.False(result.Accepted);
        Assert.Null(result.ContactNumber);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresProviderCanonicalFormNotRawInput()
    {
        _gateway.ValidateNumberAsync("(555) 123-4567", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+15551234567"));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var result = await Service().RegisterAsync(BuyerId, "(555) 123-4567");

        Assert.True(result.Accepted);
        Assert.Equal("+15551234567", result.ContactNumber!.PhoneNumber);
    }

    [Fact]
    public async Task RegistrationIsIdempotentPerShopper()
    {
        var existing = new ContactNumber(BuyerId, "+15551234567");
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+15551234567"));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { existing });

        var result = await Service().RegisterAsync(BuyerId, "+15551234567");

        Assert.True(result.Accepted);
        Assert.Same(existing, result.ContactNumber);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteReturnsFalseWhenNumberIsNotTheCallers()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var deleted = await Service().DeleteAsync(BuyerId, 42);

        Assert.False(deleted);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

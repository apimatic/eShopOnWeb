using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ContactNumberServiceTests;

public class RegisterAndDelete
{
    private readonly string _buyerId = "buyer@example.com";
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();

    private ContactNumberService Service() => new(_repo, _gateway);

    [Fact]
    public async Task RejectsNumberTheProviderConsidersUnusable()
    {
        _gateway.ValidateNumberAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null));

        await Assert.ThrowsAsync<InvalidPhoneNumberException>(
            () => Service().RegisterAsync(_buyerId, "garbage"));

        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresProviderCanonicalForm()
    {
        _gateway.ValidateNumberAsync("(202) 555-0143", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+12025550143"));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var result = await Service().RegisterAsync(_buyerId, "(202) 555-0143");

        Assert.Equal("+12025550143", result.PhoneNumber);
        await _repo.Received().AddAsync(Arg.Is<ContactNumber>(c => c.PhoneNumber == "+12025550143"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotRegisterTheSameCanonicalNumberTwice()
    {
        var existing = new ContactNumber(_buyerId, "+12025550143");
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+12025550143"));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { existing });

        var result = await Service().RegisterAsync(_buyerId, "202-555-0143");

        Assert.Same(existing, result);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletingANumberThatIsNotTheCallersIsNotFound()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => Service().DeleteAsync(_buyerId, 999));
    }
}

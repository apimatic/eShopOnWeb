using System.Collections.Generic;
using System.Threading;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _repository = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();
    private const string BuyerId = "buyer@example.com";

    private ContactNumberService CreateService() => new(_repository, _gateway, _logger);

    [Fact]
    public async Task RejectsNumberProviderConsidersUnusable()
    {
        _gateway.LookupNumberAsync("+1000", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookup(false, null));

        var result = await CreateService().RegisterAsync(BuyerId, "+1000", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ContactNumber);
        await _repository.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresProviderCanonicalFormNotRawInput()
    {
        const string raw = "(825) 555-1588";
        const string canonical = "+18255551588";
        _gateway.LookupNumberAsync(raw, Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookup(true, canonical));
        _repository.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repository.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (ContactNumber)callInfo[0]);

        var result = await CreateService().RegisterAsync(BuyerId, raw, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(canonical, result.ContactNumber!.PhoneNumber);
        await _repository.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.PhoneNumber == canonical && c.BuyerId == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteReturnsFalseWhenNumberIsNotTheCallers()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var deleted = await CreateService().DeleteAsync(BuyerId, contactNumberId: 42, CancellationToken.None);

        Assert.False(deleted);
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

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
    public async Task Register_StoresCanonicalNumberFromProvider()
    {
        var repo = Substitute.For<IRepository<ContactNumber>>();
        var lookup = Substitute.For<ITwilioLookupClient>();
        lookup.LookupAsync("555-0100", Arg.Any<CancellationToken>())
            .Returns(new LookupResult(true, "+15550100"));
        repo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);
        repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var sut = new ContactNumberService(repo, lookup);
        var result = await sut.RegisterAsync("buyer-1", "555-0100");

        Assert.Equal("+15550100", result.PhoneNumber);
        Assert.Equal("buyer-1", result.BuyerId);
    }

    [Fact]
    public async Task Register_RejectsInvalidDestination()
    {
        var repo = Substitute.For<IRepository<ContactNumber>>();
        var lookup = Substitute.For<ITwilioLookupClient>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LookupResult(false, "+1999"));

        var sut = new ContactNumberService(repo, lookup);
        await Assert.ThrowsAsync<InvalidContactNumberException>(() => sut.RegisterAsync("buyer-1", "not-a-number"));
        await repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

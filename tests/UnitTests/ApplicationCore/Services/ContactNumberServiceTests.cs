using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    [Fact]
    public async Task RegisterRejectsNumberTheProviderDoesNotConsiderUsable()
    {
        var repository = Substitute.For<IRepository<ContactNumber>>();
        var lookup = Substitute.For<ITwilioLookupClient>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult
            {
                Valid = false,
                ValidationErrors = new List<string> { "TOO_SHORT" }
            });

        var service = new ContactNumberService(repository, lookup);

        var ex = await Assert.ThrowsAsync<InvalidContactNumberException>(
            () => service.RegisterAsync("buyer-1", "123", countryCode: null));

        Assert.Contains("TOO_SHORT", ex.Message);
        await repository.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterStoresTheCanonicalNumberFromTheProvider()
    {
        var repository = Substitute.For<IRepository<ContactNumber>>();
        repository.FirstOrDefaultAsync(Arg.Any<ContactNumberByBuyerAndCanonicalNumberSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);
        repository.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var lookup = Substitute.For<ITwilioLookupClient>();
        lookup.LookupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult
            {
                Valid = true,
                CanonicalPhoneNumber = "+15555550100",
                NationalFormat = "(555) 555-0100",
                CountryCode = "US"
            });

        var service = new ContactNumberService(repository, lookup);
        var contact = await service.RegisterAsync("buyer-1", "555-555-0100", "US");

        Assert.Equal("+15555550100", contact.PhoneNumber);
        Assert.Equal("buyer-1", contact.BuyerId);
    }
}

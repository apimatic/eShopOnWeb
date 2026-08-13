using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_repo, _gateway, _logger);

    [Fact]
    public async Task RejectsNumberTheProviderConsidersInvalid()
    {
        _gateway.LookupAsync("bogus", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult(false, null, new[] { "TOO_SHORT" }));
        var service = CreateService();

        var result = await service.RegisterAsync("buyer", "bogus");

        Assert.False(result.Succeeded);
        Assert.Contains("TOO_SHORT", result.Errors);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresProviderCanonicalFormNotWhatTheCallerTyped()
    {
        _gateway.LookupAsync("(555) 123-4567", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberLookupResult(true, "+15551234567", new List<string>()));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());
        var service = CreateService();

        var result = await service.RegisterAsync("buyer", "(555) 123-4567");

        Assert.True(result.Succeeded);
        Assert.Equal("+15551234567", result.ContactNumber!.PhoneNumber);
    }

    [Fact]
    public async Task DoesNotDeleteAnotherShoppersNumber()
    {
        var othersNumber = new ContactNumber("someone-else", "+15551234567");
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(othersNumber);
        var service = CreateService();

        var deleted = await service.DeleteAsync("buyer", 5);

        Assert.False(deleted);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

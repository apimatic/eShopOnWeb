using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _contactRepo = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifRepo = Substitute.For<IRepository<Notification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_contactRepo, _notifRepo, _gateway, _logger);

    [Fact]
    public async Task Register_WhenProviderRejectsNumber_DoesNotStoreIt()
    {
        _gateway.LookupNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(false, null));

        var result = await CreateService().RegisterAsync("owner", "garbage", default);

        Assert.Equal(ContactNumberRegistrationOutcome.Rejected, result.Outcome);
        await _contactRepo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenValid_StoresProviderCanonicalForm()
    {
        _gateway.LookupNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(true, "+15551230000"));
        _contactRepo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<ContactNumber>());

        var result = await CreateService().RegisterAsync("owner", "(555) 123-0000", default);

        Assert.Equal(ContactNumberRegistrationOutcome.Registered, result.Outcome);
        // Stored value is the provider's canonical E.164 form, not the raw input.
        await _contactRepo.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.E164Number == "+15551230000" && c.OwnerId == "owner"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_WhenNumberIsNotTheCallers_ReturnsFalseAndDeletesNothing()
    {
        _contactRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var removed = await CreateService().RemoveAsync("owner", 42, default);

        Assert.False(removed);
        await _contactRepo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

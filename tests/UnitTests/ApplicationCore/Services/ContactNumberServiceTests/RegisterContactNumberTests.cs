using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ContactNumberServiceTests;

public class RegisterContactNumberTests
{
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();

    private ContactNumberService CreateService() => new(_repo, _sms);

    [Fact]
    public async Task RejectsNumberTheProviderCannotUse()
    {
        _sms.ValidateNumberAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidation(false, null));

        var result = await CreateService().RegisterAsync("buyer", "garbage");

        Assert.Equal(RegisterContactNumberError.NotAUsableDestination, result.Error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresProviderCanonicalFormNotWhatWasTyped()
    {
        _sms.ValidateNumberAsync("(415) 555-0100", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidation(true, "+14155550100"));
        _repo.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await CreateService().RegisterAsync("buyer", "(415) 555-0100");

        Assert.True(result.Succeeded);
        Assert.Equal("+14155550100", result.CanonicalNumber);
        await _repo.Received().AddAsync(
            Arg.Is<ContactNumber>(c => c.E164Number == "+14155550100" && c.OwnerId == "buyer"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisteringAnExistingNumberIsIdempotent()
    {
        var existing = new ContactNumber("buyer", "+14155550100");
        _sms.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidation(true, "+14155550100"));
        _repo.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { existing });

        var result = await CreateService().RegisterAsync("buyer", "+14155550100");

        Assert.True(result.Succeeded);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SmsNotifications;

public class ContactNumberServiceTests
{
    private const string Owner = "demouser@microsoft.com";
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_repo, _sms, _logger);

    [Fact]
    public async Task RegisterStoresProviderCanonicalForm()
    {
        _sms.ValidateAsync("514 555 1234", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+15145551234", Array.Empty<string>()));
        _repo.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        ContactNumber? added = null;
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => { added = ci.Arg<ContactNumber>(); return added; });

        var result = await CreateService().RegisterAsync(Owner, "514 555 1234");

        Assert.Equal("+15145551234", result.PhoneNumber); // canonical, not what was typed
        Assert.NotNull(added);
        await _repo.Received(1).AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterRejectsAnUnusableNumberAtRegistrationTime()
    {
        _sms.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null, new[] { "not_a_number" }));

        await Assert.ThrowsAsync<InvalidPhoneNumberException>(
            () => CreateService().RegisterAsync(Owner, "nonsense"));

        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveOfANumberThatIsNotTheCallersReturnsFalseAndDeletesNothing()
    {
        // Scoped spec finds nothing for this owner+id.
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdAndOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var removed = await CreateService().RemoveAsync(Owner, 999);

        Assert.False(removed);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

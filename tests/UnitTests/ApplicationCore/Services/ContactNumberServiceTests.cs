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
    private const string Owner = "shopper-1";
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsNotificationService _sms = Substitute.For<ISmsNotificationService>();
    private readonly ContactNumberService _service;

    public ContactNumberServiceTests()
    {
        _service = new ContactNumberService(_repo, _sms);
        _repo.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
    }

    [Fact]
    public async Task StoresProviderCanonicalForm_WhenValid()
    {
        _sms.ValidatePhoneNumberAsync("0614 hint", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidationResult(true, "+15551234567", null));

        var result = await _service.RegisterAsync(Owner, "0614 hint");

        Assert.True(result.Success);
        Assert.Equal("+15551234567", result.ContactNumber!.PhoneNumber); // canonical, not what was typed
        await _repo.Received(1).AddAsync(Arg.Is<ContactNumber>(c => c.PhoneNumber == "+15551234567"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnusableNumber_WithoutStoring()
    {
        _sms.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidationResult(false, null, "The number is not a valid destination."));

        var result = await _service.RegisterAsync(Owner, "garbage");

        Assert.False(result.Success);
        Assert.Equal("The number is not a valid destination.", result.Error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsIdempotentForAnAlreadyRegisteredCanonicalNumber()
    {
        var existing = new ContactNumber(Owner, "+15551234567");
        _repo.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { existing });
        _sms.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidationResult(true, "+15551234567", null));

        var result = await _service.RegisterAsync(Owner, "+1 (555) 123-4567");

        Assert.True(result.Success);
        Assert.Same(existing, result.ContactNumber);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_RemovesAnOwnedNumber()
    {
        var owned = new ContactNumber(Owner, "+15551234567");
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByOwnerAndIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(owned);

        var deleted = await _service.DeleteAsync(Owner, 5);

        Assert.True(deleted);
        await _repo.Received(1).DeleteAsync(owned, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ReturnsFalse_WhenNotOwnedOrMissing()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByOwnerAndIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var deleted = await _service.DeleteAsync(Owner, 99);

        Assert.False(deleted);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

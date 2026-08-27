using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsService _sms = Substitute.For<ISmsService>();

    private ContactNumberService Sut => new(_contacts, _sms);

    [Fact]
    public async Task Register_ProviderRejectsNumber_ThrowsBadRequest()
    {
        _sms.ValidatePhoneNumberAsync(Arg.Any<string>())
            .Returns(new PhoneNumberValidationResult(false, null, "TOO_SHORT"));

        await Assert.ThrowsAsync<BadRequestException>(() => Sut.RegisterAsync("buyer", "123"));
        await _contacts.DidNotReceive().AddAsync(Arg.Any<ContactNumber>());
    }

    [Fact]
    public async Task Register_ValidNumber_StoresCanonicalForm()
    {
        _sms.ValidatePhoneNumberAsync(Arg.Any<string>())
            .Returns(new PhoneNumberValidationResult(true, "+14155552671", null));
        _contacts.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _contacts.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var result = await Sut.RegisterAsync("buyer", "415-555-2671");

        Assert.Equal("+14155552671", result.PhoneNumber);
        Assert.Equal("buyer", result.OwnerId);
    }

    [Fact]
    public async Task Register_DuplicateNumber_ThrowsDuplicate()
    {
        _sms.ValidatePhoneNumberAsync(Arg.Any<string>())
            .Returns(new PhoneNumberValidationResult(true, "+14155552671", null));
        _contacts.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer", "+14155552671") });

        await Assert.ThrowsAsync<DuplicateException>(() => Sut.RegisterAsync("buyer", "+14155552671"));
    }

    [Fact]
    public async Task Delete_OtherShoppersNumber_NotFound()
    {
        _contacts.GetByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(new ContactNumber("someone-else", "+14155552671"));

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Sut.DeleteAsync("buyer", 3));
        await _contacts.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>());
    }
}

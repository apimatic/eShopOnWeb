using System.Collections.Generic;
using System.Threading;
using Ardalis.Result;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ContactNumberServiceTests
{
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly IPhoneNumberValidator _validator = Substitute.For<IPhoneNumberValidator>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_repo, _validator, _logger);

    [Fact]
    public async System.Threading.Tasks.Task Register_RejectsNumberProviderDeemsUnusable()
    {
        _validator.ValidateAsync("garbage", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidationResult { IsValid = false, Errors = new[] { "NOT_A_NUMBER" } });

        var result = await CreateService().RegisterAsync("buyer1", "garbage");

        Assert.Equal(ResultStatus.Invalid, result.Status);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Register_StoresProviderCanonicalForm()
    {
        _validator.ValidateAsync("(206) 555-0123", Arg.Any<CancellationToken>())
            .Returns(new PhoneNumberValidationResult { IsValid = true, CanonicalNumber = "+12065550123" });
        _repo.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        var result = await CreateService().RegisterAsync("buyer1", "(206) 555-0123");

        Assert.True(result.IsSuccess);
        Assert.Equal("+12065550123", result.Value.PhoneNumber);
        await _repo.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.PhoneNumber == "+12065550123" && c.BuyerId == "buyer1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async System.Threading.Tasks.Task Delete_ReturnsNotFound_WhenNumberNotOwnedByCaller()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var result = await CreateService().DeleteAsync("buyer1", 42);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

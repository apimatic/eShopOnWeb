using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class ContactNumberServiceTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<ContactNumber> _repository;
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly ContactNumberService _service;

    public ContactNumberServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"ContactNumbers-{System.Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
        _repository = new EfRepository<ContactNumber>(_context);
        _service = new ContactNumberService(_repository, _gateway);
    }

    [Fact]
    public async Task Register_StoresProviderCanonicalForm_NotWhatCallerTyped()
    {
        _gateway.ValidateNumberAsync("416 555 1234", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165551234"));

        var result = await _service.RegisterAsync("shopper@x.com", "416 555 1234");

        Assert.True(result.Succeeded);
        Assert.Equal("+14165551234", result.ContactNumber!.E164Number);
    }

    [Fact]
    public async Task Register_RejectsUnusableNumber_AtRegistration()
    {
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null));

        var result = await _service.RegisterAsync("shopper@x.com", "not-a-number");

        Assert.False(result.Succeeded);
        Assert.Null(result.ContactNumber);
        Assert.Empty(await _service.ListAsync("shopper@x.com"));
    }

    [Fact]
    public async Task List_And_Delete_AreOwnerScoped()
    {
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165550001"));
        var mine = await _service.RegisterAsync("owner-a", "1");

        // Another shopper cannot delete owner-a's number.
        var deletedByOther = await _service.DeleteAsync("owner-b", mine.ContactNumber!.Id);
        Assert.False(deletedByOther);
        Assert.Single(await _service.ListAsync("owner-a"));

        // The owner can, and afterwards it is gone.
        var deletedByOwner = await _service.DeleteAsync("owner-a", mine.ContactNumber!.Id);
        Assert.True(deletedByOwner);
        Assert.Empty(await _service.ListAsync("owner-a"));
    }

    [Fact]
    public async Task Register_IsIdempotent_ForSameCanonicalNumber()
    {
        _gateway.ValidateNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165550002"));

        var first = await _service.RegisterAsync("owner-a", "416-555-0002");
        var second = await _service.RegisterAsync("owner-a", "(416) 555 0002");

        Assert.Equal(first.ContactNumber!.Id, second.ContactNumber!.Id);
        Assert.Single(await _service.ListAsync("owner-a"));
    }
}

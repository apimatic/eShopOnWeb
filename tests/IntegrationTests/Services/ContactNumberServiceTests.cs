using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class ContactNumberServiceTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<ContactNumber> _repository;
    private readonly ISmsProvider _smsProvider = Substitute.For<ISmsProvider>();
    private readonly ContactNumberService _service;

    public ContactNumberServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "ContactNumberServiceTests-" + System.Guid.NewGuid())
            .Options;
        _context = new CatalogContext(options);
        _repository = new EfRepository<ContactNumber>(_context);
        _service = new ContactNumberService(_repository, _smsProvider, Substitute.For<IAppLogger<ContactNumberService>>());
    }

    [Fact]
    public async Task StoresProviderCanonicalForm_WhenValid()
    {
        _smsProvider.ValidateAsync("0825 475 1588", Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+18254751588"));

        var result = await _service.RegisterAsync("buyer-1", "0825 475 1588");

        Assert.NotNull(result);
        Assert.Equal("+18254751588", result!.PhoneNumber); // canonical form, not the raw input
        var stored = await _repository.GetByIdAsync(result.Id);
        Assert.Equal("+18254751588", stored!.PhoneNumber);
    }

    [Fact]
    public async Task RejectsAtRegistration_WhenProviderSaysUnusable()
    {
        _smsProvider.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null));

        var result = await _service.RegisterAsync("buyer-1", "not-a-real-number");

        Assert.Null(result);           // rejected here, not when a later message fails
        Assert.Empty(await _repository.ListAsync());
    }

    [Fact]
    public async Task RemoveIsScopedToOwner()
    {
        _smsProvider.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+18254751588"));
        var mine = await _service.RegisterAsync("owner", "x");

        // A different shopper cannot remove it.
        Assert.False(await _service.RemoveAsync("someone-else", mine!.Id));
        Assert.NotNull(await _repository.GetByIdAsync(mine.Id));

        // The owner can, and afterwards it is gone.
        Assert.True(await _service.RemoveAsync("owner", mine.Id));
        Assert.Null(await _repository.GetByIdAsync(mine.Id));
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ContactNumberServiceTests;

public class RegisterAndDelete
{
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsProvider _provider = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService Service() => new(_repo, _provider, _logger);

    [Fact]
    public async Task RejectsNumberTheProviderDoesNotConsiderUsable()
    {
        _provider.LookupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(false, null, null, new[] { "TOO_SHORT" }));

        var result = await Service().RegisterAsync("buyer1", "+1555", null);

        Assert.False(result.Success);
        Assert.Contains("TOO_SHORT", result.Errors);
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresTheProvidersCanonicalForm()
    {
        _provider.LookupAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(true, "+18254751588", "(825) 475-1588", Array.Empty<string>()));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        _repo.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ContactNumber>());

        // caller typed a messy national form; provider canonicalised it
        var result = await Service().RegisterAsync("buyer1", "(825) 475-1588", "CA");

        Assert.True(result.Success);
        Assert.Equal("+18254751588", result.ContactNumber!.PhoneNumber);
        await _repo.Received(1).AddAsync(Arg.Is<ContactNumber>(c => c.PhoneNumber == "+18254751588"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteScopedToOwnerReturnsFalseWhenNotTheirs()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<ContactNumberByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var removed = await Service().DeleteAsync("buyer1", 42);

        Assert.False(removed);
        await _repo.DidNotReceive().DeleteAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }
}

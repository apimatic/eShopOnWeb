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
    private readonly IRepository<ContactNumber> _repo = Substitute.For<IRepository<ContactNumber>>();
    private readonly ITwilioMessagingGateway _gateway = Substitute.For<ITwilioMessagingGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    private ContactNumberService CreateService() => new(_repo, _gateway, _logger);

    [Fact]
    public async Task RejectsANumberTheProviderDoesNotConsiderUsable()
    {
        _gateway.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(false, null, "not a valid destination"));

        var result = await CreateService().RegisterAsync("buyer@test.com", "+1555", CancellationToken.None);

        Assert.False(result.Success);
        // Rejected at registration — nothing is stored.
        await _repo.DidNotReceive().AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoresTheProviderCanonicalFormWhenValid()
    {
        _gateway.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+15551234567", null));
        _repo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await CreateService().RegisterAsync("buyer@test.com", "555 123 4567", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("+15551234567", result.CanonicalNumber);
        await _repo.Received(1).AddAsync(
            Arg.Is<ContactNumber>(c => c.PhoneNumber == "+15551234567"), Arg.Any<CancellationToken>());
    }
}

using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.ContactNumberServiceTests;

public class Register
{
    private readonly IRepository<ContactNumber> _repository = Substitute.For<IRepository<ContactNumber>>();
    private readonly ISmsNotificationGateway _sms = Substitute.For<ISmsNotificationGateway>();
    private readonly IAppLogger<ContactNumberService> _logger = Substitute.For<IAppLogger<ContactNumberService>>();

    [Fact]
    public async Task StoresCanonicalNumberFromProvider()
    {
        _sms.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(true, "+15555550100", System.Array.Empty<string>()));
        _repository.FirstOrDefaultAsync(Arg.Any<ContactNumberByBuyerAndCanonicalSpec>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);
        _repository.AddAsync(Arg.Any<ContactNumber>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ContactNumber>());

        var service = new ContactNumberService(_repository, _sms, _logger);
        var result = await service.RegisterAsync("demouser@microsoft.com", "555-0100", CancellationToken.None);

        Assert.Equal("+15555550100", result.CanonicalNumber);
        await _repository.Received().AddAsync(Arg.Is<ContactNumber>(c => c.CanonicalNumber == "+15555550100"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnusableNumber()
    {
        _sms.LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneLookupResult(false, null, new[] { "NOT_A_NUMBER" }));

        var service = new ContactNumberService(_repository, _sms, _logger);

        await Assert.ThrowsAsync<InvalidContactNumberException>(
            () => service.RegisterAsync("demouser@microsoft.com", "not-a-number", CancellationToken.None));
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}

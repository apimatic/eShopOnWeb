using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.MaxioSubscriptionServiceTests;

public class GetMySubscriptionsAsync
{
    private const string BuyerId = "buyer@example.com";

    private readonly IMaxioClient _mockMaxioClient = Substitute.For<IMaxioClient>();
    private readonly IRepository<MaxioCustomerLink> _mockLinkRepo = Substitute.For<IRepository<MaxioCustomerLink>>();

    private readonly MaxioSubscriptionService _service;

    public GetMySubscriptionsAsync()
    {
        _service = new MaxioSubscriptionService(_mockMaxioClient, _mockLinkRepo);
    }

    [Fact]
    public async Task NoLocalCustomerLink_ReturnsEmptyWithoutCallingMaxio()
    {
        _mockLinkRepo.FirstOrDefaultAsync(Arg.Any<MaxioCustomerLinkByBuyerIdSpecification>(), default)
            .Returns((MaxioCustomerLink?)null);

        var result = await _service.GetMySubscriptionsAsync(BuyerId);

        Assert.Empty(result);
        await _mockMaxioClient.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task CustomerLinkExists_ReturnsMaxioSubscriptionsForThatCustomer()
    {
        var link = new MaxioCustomerLink(BuyerId, 42);
        _mockLinkRepo.FirstOrDefaultAsync(Arg.Any<MaxioCustomerLinkByBuyerIdSpecification>(), default)
            .Returns(link);
        _mockMaxioClient.ListCustomerSubscriptionsAsync(42)
            .Returns(new List<MaxioSubscriptionDto>
            {
                new() { Id = 1, CustomerId = 42, ProductHandle = "eshop-pro", State = "active" }
            });

        var result = await _service.GetMySubscriptionsAsync(BuyerId);

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].ProductHandle);
    }
}

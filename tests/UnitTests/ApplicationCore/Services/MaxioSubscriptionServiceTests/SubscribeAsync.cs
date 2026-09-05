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

public class SubscribeAsync
{
    private const string BuyerId = "buyer@example.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IMaxioClient _mockMaxioClient = Substitute.For<IMaxioClient>();
    private readonly IRepository<MaxioCustomerLink> _mockLinkRepo = Substitute.For<IRepository<MaxioCustomerLink>>();

    private readonly MaxioSubscriptionService _service;

    public SubscribeAsync()
    {
        _service = new MaxioSubscriptionService(_mockMaxioClient, _mockLinkRepo);
    }

    [Fact]
    public async Task NewBuyer_EnsuresCustomerAndCreatesSubscription()
    {
        _mockLinkRepo.FirstOrDefaultAsync(Arg.Any<MaxioCustomerLinkByBuyerIdSpecification>(), default)
            .Returns((MaxioCustomerLink?)null);
        _mockMaxioClient.EnsureCustomerAsync(BuyerId, BuyerId, Arg.Any<string>(), Arg.Any<string>())
            .Returns(new MaxioCustomerDto { Id = 42, Reference = BuyerId, Email = BuyerId });
        _mockMaxioClient.ListCustomerSubscriptionsAsync(42)
            .Returns(new List<MaxioSubscriptionDto>());
        _mockMaxioClient.CreateSubscriptionAsync(42, PlanHandle)
            .Returns(new MaxioSubscriptionDto { Id = 1, CustomerId = 42, ProductHandle = PlanHandle, State = "active" });

        var result = await _service.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(1, result.Id);
        await _mockMaxioClient.Received(1).CreateSubscriptionAsync(42, PlanHandle);
        await _mockLinkRepo.Received(1).AddAsync(
            Arg.Is<MaxioCustomerLink>(link => link.BuyerId == BuyerId && link.MaxioCustomerId == 42),
            default);
    }

    [Fact]
    public async Task LiveSubscriptionAlreadyExistsForPlan_ReturnsExistingWithoutCreatingAnother()
    {
        var link = new MaxioCustomerLink(BuyerId, 42);
        _mockLinkRepo.FirstOrDefaultAsync(Arg.Any<MaxioCustomerLinkByBuyerIdSpecification>(), default)
            .Returns(link);

        var existingSubscription = new MaxioSubscriptionDto { Id = 7, CustomerId = 42, ProductHandle = PlanHandle, State = "active" };
        _mockMaxioClient.ListCustomerSubscriptionsAsync(42)
            .Returns(new List<MaxioSubscriptionDto> { existingSubscription });

        var result = await _service.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(7, result.Id);
        await _mockMaxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task OnlyCanceledSubscriptionExistsForPlan_CreatesANewOne()
    {
        var link = new MaxioCustomerLink(BuyerId, 42);
        _mockLinkRepo.FirstOrDefaultAsync(Arg.Any<MaxioCustomerLinkByBuyerIdSpecification>(), default)
            .Returns(link);

        var canceledSubscription = new MaxioSubscriptionDto { Id = 7, CustomerId = 42, ProductHandle = PlanHandle, State = "canceled" };
        _mockMaxioClient.ListCustomerSubscriptionsAsync(42)
            .Returns(new List<MaxioSubscriptionDto> { canceledSubscription });
        _mockMaxioClient.CreateSubscriptionAsync(42, PlanHandle)
            .Returns(new MaxioSubscriptionDto { Id = 9, CustomerId = 42, ProductHandle = PlanHandle, State = "active" });

        var result = await _service.SubscribeAsync(BuyerId, BuyerId, PlanHandle);

        Assert.Equal(9, result.Id);
        await _mockMaxioClient.Received(1).CreateSubscriptionAsync(42, PlanHandle);
    }
}

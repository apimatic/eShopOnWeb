using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Endpoints;

public class CreateSubscriptionEndpointTests
{
    private static readonly SubscriberProfile Shopper =
        new("user-1", "demouser@microsoft.com", "Demo", "Shopper");

    [Fact]
    public async Task Answers_201_when_the_shopper_is_newly_enrolled()
    {
        var service = Substitute.For<ISubscriptionService>();
        service.SubscribeAsync(Arg.Any<SubscribeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result(created: true));

        var result = await new CreateSubscriptionEndpoint().HandleAsync(Request("eshop-pro"), service);

        var created = Assert.IsType<Created<CreateSubscriptionResponse>>(result);
        Assert.True(created.Value!.Created);
        Assert.Equal("eshop-pro", created.Value.Subscription!.PlanHandle);
        Assert.True(created.Value.Subscription.IsActive);
    }

    [Fact]
    public async Task Answers_200_when_the_shopper_was_already_enrolled()
    {
        var service = Substitute.For<ISubscriptionService>();
        service.SubscribeAsync(Arg.Any<SubscribeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result(created: false));

        var result = await new CreateSubscriptionEndpoint().HandleAsync(Request("eshop-pro"), service);

        var ok = Assert.IsType<Ok<CreateSubscriptionResponse>>(result);
        Assert.False(ok.Value!.Created);
    }

    [Fact]
    public async Task Rejects_a_request_without_a_plan_handle()
    {
        var service = Substitute.For<ISubscriptionService>();

        var result = await new CreateSubscriptionEndpoint().HandleAsync(Request("  "), service);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        await service.DidNotReceiveWithAnyArgs().SubscribeAsync(default!, default);
    }

    [Fact]
    public async Task Rejects_a_token_that_no_longer_maps_to_a_user()
    {
        var service = Substitute.For<ISubscriptionService>();

        var request = Request("eshop-pro");
        request.Subscriber = null;

        var result = await new CreateSubscriptionEndpoint().HandleAsync(request, service);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await service.DidNotReceiveWithAnyArgs().SubscribeAsync(default!, default);
    }

    [Fact]
    public async Task Passes_the_idempotency_key_and_the_token_identity_through_to_the_service()
    {
        var service = Substitute.For<ISubscriptionService>();
        service.SubscribeAsync(Arg.Any<SubscribeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result(created: true));

        var request = Request("  eshop-pro  ");
        request.IdempotencyKey = "order-4711";

        await new CreateSubscriptionEndpoint().HandleAsync(request, service);

        await service.Received(1).SubscribeAsync(
            Arg.Is<SubscribeCommand>(command =>
                command.PlanHandle == "eshop-pro"
                && command.IdempotencyKey == "order-4711"
                && command.Subscriber.Email == Shopper.Email),
            Arg.Any<CancellationToken>());
    }

    private static CreateSubscriptionRequest Request(string planHandle) => new()
    {
        PlanHandle = planHandle,
        Subscriber = Shopper
    };

    private static SubscribeResult Result(bool created) => new(
        new CustomerSubscription
        {
            Id = 42,
            State = SubscriptionState.Active,
            RawState = "active",
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            CustomerId = 7,
            NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
        },
        created,
        CustomerCreated: created,
        CustomerReference: "eshoponweb-demouser-microsoft-com-03563e80");
}

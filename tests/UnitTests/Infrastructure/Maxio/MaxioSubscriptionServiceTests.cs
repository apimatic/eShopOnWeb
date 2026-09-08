using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private const string FAMILY = "eshop-subscribe";

    private static MaxioCustomerInput Customer() => new()
    {
        Reference = "buyer@eshop.local",
        Email = "buyer@eshop.local",
        FirstName = "Buyer",
        LastName = "One"
    };

    private static SubscriptionPlan Plan(string handle, string family = FAMILY, bool archived = false) => new()
    {
        Id = handle.GetHashCode() & 0x7fffffff,
        Handle = handle,
        Name = handle,
        ProductFamilyHandle = family,
        Archived = archived,
        PriceInCents = 100
    };

    private static Subscription Sub(long id, string planHandle, string state = "active") => new()
    {
        Id = id,
        State = state,
        ProductHandle = planHandle,
        CustomerId = 555,
        PriceInCents = 100
    };

    // ---- Plans ----------------------------------------------------------------------

    [Fact]
    public async Task GetAvailablePlans_ScopesToFamilyAndExcludesArchived()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.ListProductsAsync(Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan>
        {
            Plan("eshop-pro"),
            Plan("basic-plan"),
            Plan("archived-plan", archived: true),
            Plan("other-family-plan", family: "unrelated")
        });

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.GetAvailablePlansAsync(FAMILY);

        var handles = result.Select(p => p.Handle).OrderBy(h => h).ToList();
        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, handles);
        Assert.DoesNotContain(result, p => p.Handle == "archived-plan");
        Assert.DoesNotContain(result, p => p.Handle == "other-family-plan");
    }

    [Fact]
    public async Task GetAvailablePlans_RequiresFamilyHandle()
    {
        var client = Substitute.For<IMaxioApiClient>();
        var svc = new MaxioSubscriptionService(client);

        await Assert.ThrowsAsync<ArgumentException>(async () => await svc.GetAvailablePlansAsync("  "));
    }

    // ---- Subscribe (happy path) -----------------------------------------------------

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync("buyer@eshop.local", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        client.CreateCustomerAsync(Arg.Any<MaxioCustomerInput>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" });
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Subscription?)null);
        client.ListSubscriptionsForCustomerAsync(555, Arg.Any<CancellationToken>()).Returns(new List<Subscription>());
        client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Sub(999, "eshop-pro"));

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.SubscribeAsync(Customer(), "eshop-pro");

        Assert.Equal(999, result.Id);
        await client.Received(1).CreateCustomerAsync(Arg.Is<MaxioCustomerInput>(c => c.Reference == "buyer@eshop.local"), Arg.Any<CancellationToken>());
        await client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionInput>(s => s.CustomerId == 555 && s.ProductHandle == "eshop-pro"
                && s.Reference == "buyer@eshop.local:eshop-pro" && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesExistingCustomer_NoDuplicateCreate()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync("buyer@eshop.local", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" });
        client.FindSubscriptionByReferenceAsync("buyer@eshop.local:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Sub(999, "eshop-pro"));

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.SubscribeAsync(Customer(), "eshop-pro");

        Assert.Equal(999, result.Id);
        await client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCustomerInput>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_DoubleClickIsIdempotent_ReturnsExistingSubscription()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" });
        // The deterministic reference already exists -> short-circuit, never create.
        client.FindSubscriptionByReferenceAsync("buyer@eshop.local:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Sub(999, "eshop-pro"));

        var svc = new MaxioSubscriptionService(client);
        var first = await svc.SubscribeAsync(Customer(), "eshop-pro");
        var second = await svc.SubscribeAsync(Customer(), "eshop-pro");

        Assert.Equal(999, first.Id);
        Assert.Equal(first.Id, second.Id);
        await client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesLiveSubscription_WhenReferenceLookupMisses()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" });
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Subscription?)null);
        client.ListSubscriptionsForCustomerAsync(555, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { Sub(777, "eshop-pro", state: "active") });

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.SubscribeAsync(Customer(), "eshop-pro");

        Assert.Equal(777, result.Id);
        await client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_RecoversFromCustomerReferenceRace()
    {
        var client = Substitute.For<IMaxioApiClient>();
        // First lookup: not found. After the 422 (another request created it), the second lookup finds it.
        client.FindCustomerByReferenceAsync("buyer@eshop.local", Arg.Any<CancellationToken>())
            .Returns(null!, new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" }!);
        client.CreateCustomerAsync(Arg.Any<MaxioCustomerInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<MaxioCustomer>>(_ => throw new MaxioApiException(422, "{}", "reference taken"));
        client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Subscription?)null);
        client.ListSubscriptionsForCustomerAsync(555, Arg.Any<CancellationToken>()).Returns(new List<Subscription>());
        client.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionInput>(), Arg.Any<CancellationToken>())
            .Returns(Sub(999, "eshop-pro"));

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.SubscribeAsync(Customer(), "eshop-pro");

        Assert.Equal(999, result.Id);
        await client.Received(1).CreateCustomerAsync(Arg.Any<MaxioCustomerInput>(), Arg.Any<CancellationToken>());
        await client.Received(2).FindCustomerByReferenceAsync("buyer@eshop.local", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_RejectsMissingInputs()
    {
        var client = Substitute.For<IMaxioApiClient>();
        var svc = new MaxioSubscriptionService(client);

        await Assert.ThrowsAsync<ArgumentException>(async () => await svc.SubscribeAsync(Customer(), ""));
        await Assert.ThrowsAsync<ArgumentException>(async () => await svc.SubscribeAsync(new MaxioCustomerInput { Email = "x@y.z", Reference = "" }, "eshop-pro"));
    }

    // ---- My subscriptions -----------------------------------------------------------

    [Fact]
    public async Task GetSubscriptions_EmptyWhenCustomerUnknown()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync("nobody@eshop.local", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.GetSubscriptionsForCustomerAsync("nobody@eshop.local");

        Assert.Empty(result);
        await client.DidNotReceive().ListSubscriptionsForCustomerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsCustomerSubscriptions()
    {
        var client = Substitute.For<IMaxioApiClient>();
        client.FindCustomerByReferenceAsync("buyer@eshop.local", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 555, Reference = "buyer@eshop.local" });
        client.ListSubscriptionsForCustomerAsync(555, Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { Sub(1, "eshop-pro"), Sub(2, "basic-plan") });

        var svc = new MaxioSubscriptionService(client);
        var result = await svc.GetSubscriptionsForCustomerAsync("buyer@eshop.local");

        Assert.Equal(2, result.Count);
    }
}

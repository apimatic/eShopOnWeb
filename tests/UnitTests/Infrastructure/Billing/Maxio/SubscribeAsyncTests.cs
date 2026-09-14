using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class SubscribeAsyncTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSubscriptionBillingService _service;

    public SubscribeAsyncTests()
    {
        _client.GetSiteAsync(Arg.Any<CancellationToken>()).Returns(MaxioTestData.Site());
        _client.ListProductsForFamilyAsync(MaxioTestData.FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                MaxioTestData.Product(MaxioTestData.ProPlanHandle, "Pro Plan", 29900),
                MaxioTestData.Product(MaxioTestData.BasicPlanHandle, "Basic Plan", 2900)
            });

        _service = new MaxioSubscriptionBillingService(
            _client,
            MaxioTestData.Settings(),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static SubscribeRequest Request(string planHandle = MaxioTestData.ProPlanHandle, string? idempotencyKey = null) =>
        new(MaxioTestData.UserName, planHandle) { Email = MaxioTestData.UserName, IdempotencyKey = idempotencyKey };

    [Fact]
    public async Task CreatesCustomerKeyedOnTheUserNameWhenNoneExists()
    {
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Customer());
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        await _service.SubscribeAsync(Request());

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(r =>
                r.Customer.Reference == MaxioTestData.CustomerReference &&
                r.Customer.Email == MaxioTestData.UserName &&
                // Maxio rejects a blank first or last name, so both must always be populated.
                !string.IsNullOrWhiteSpace(r.Customer.FirstName) &&
                !string.IsNullOrWhiteSpace(r.Customer.LastName)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesTheExistingCustomerRatherThanCreatingASecond()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        await _service.SubscribeAsync(Request());

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenAConcurrentRequestClaimedTheCustomerReferenceFirst()
    {
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, MaxioTestData.Customer());
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(MaxioTestData.ReferenceTaken("POST", "customers.json"));
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        var result = await _service.SubscribeAsync(Request());

        Assert.True(result.Created);
        Assert.Equal(MaxioTestData.Customer().Id, result.Subscription.BillingCustomerId);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionWithoutCreatingAnother()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Subscription(id: 555, state: "active") });

        var result = await _service.SubscribeAsync(Request());

        Assert.False(result.Created);
        Assert.Equal(555, result.Subscription.Id);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    public async Task TreatsNonActiveButStillEnrolledStatesAsAlreadySubscribed(string state)
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Subscription(state: state) });

        var result = await _service.SubscribeAsync(Request());

        Assert.False(result.Created);
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    public async Task SubscribesAgainAfterAPreviousSubscriptionEnded(string endedState)
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Subscription(id: 111, state: endedState) });
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription(id: 222));

        var result = await _service.SubscribeAsync(Request());

        Assert.True(result.Created);
        Assert.Equal(222, result.Subscription.Id);

        // The ended subscription still occupies generation 0, so the new one must not reuse it.
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.Reference!.EndsWith("|1", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendsAReferenceThatIsStableForTheSameIntent()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        await _service.SubscribeAsync(Request());
        await _service.SubscribeAsync(Request());

        var references = _client.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMaxioApiClient.CreateSubscriptionAsync))
            .Select(c => ((MaxioCreateSubscriptionRequest)c.GetArguments()[0]!).Subscription.Reference)
            .ToList();

        Assert.Equal(2, references.Count);
        Assert.Single(references.Distinct());
        Assert.Equal($"{MaxioTestData.CustomerReference}|{MaxioTestData.ProPlanHandle}|0", references[0]);
    }

    [Fact]
    public async Task ACallerSuppliedIdempotencyKeyScopesTheReference()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        await _service.SubscribeAsync(Request(idempotencyKey: "checkout-42"));

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.Reference == $"{MaxioTestData.CustomerReference}|{MaxioTestData.ProPlanHandle}|checkout-42"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadsBackTheWinnerWhenAnotherInstanceClaimedTheSubscriptionReference()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(MaxioTestData.ReferenceTaken("POST", "subscriptions.json"));
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription(id: 999));

        var result = await _service.SubscribeAsync(Request());

        Assert.False(result.Created);
        Assert.Equal(999, result.Subscription.Id);
    }

    [Fact]
    public async Task ReportsAnInFlightDuplicateWhenTheWinnerCannotBeReadBack()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(MaxioTestData.ReferenceTaken("POST", "subscriptions.json"));
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);

        await Assert.ThrowsAsync<DuplicateSubscribeRequestException>(() => _service.SubscribeAsync(Request()));
    }

    [Fact]
    public async Task ConcurrentSubscribesForOneUserProduceASingleSubscription()
    {
        GivenCustomerExists();

        var created = MaxioTestData.Subscription(id: 4242);
        var createCalls = 0;
        var stored = new List<MaxioSubscription>();

        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => stored.ToArray());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref createCalls);
                stored.Add(created);
                return Task.FromResult(created);
            });

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => _service.SubscribeAsync(Request())));

        Assert.Equal(1, createCalls);
        Assert.Single(results.Where(r => r.Created));
        Assert.All(results, r => Assert.Equal(4242, r.Subscription.Id));
    }

    [Fact]
    public async Task RejectsAnUnknownPlanWithoutTouchingCustomerRecords()
    {
        GivenCustomerExists();

        await Assert.ThrowsAsync<PlanNotFoundException>(() => _service.SubscribeAsync(Request("not-a-plan")));

        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAnArchivedPlan()
    {
        _client.ListProductsForFamilyAsync(MaxioTestData.FamilyHandle, Arg.Any<CancellationToken>())
            .Returns(new[] { MaxioTestData.Product(archivedAt: DateTimeOffset.UtcNow) });

        await Assert.ThrowsAsync<PlanNotFoundException>(() => _service.SubscribeAsync(Request()));
    }

    [Fact]
    public async Task SurfacesAProviderRejectionAsAValidationFailure()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "POST", "subscriptions.json",
                new[] { "No payment method was on file for the $299.00 balance" }));

        var exception = await Assert.ThrowsAsync<BillingValidationException>(() => _service.SubscribeAsync(Request()));

        Assert.Contains("No payment method was on file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfacesAnOutageAsAProviderFailure()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(System.Net.HttpStatusCode.ServiceUnavailable, "POST", "subscriptions.json",
                Array.Empty<string>()));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => _service.SubscribeAsync(Request()));

        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task ConfirmsPlanPriceStateAndNextBillingDateBackToTheCaller()
    {
        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        var result = await _service.SubscribeAsync(Request());

        Assert.Equal(MaxioTestData.ProPlanHandle, result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 9, 36, 49, TimeSpan.FromHours(5)), result.Subscription.NextBillingAt);
    }

    [Fact]
    public async Task UsesTheConfiguredPaymentCollectionMethod()
    {
        var service = new MaxioSubscriptionBillingService(
            _client,
            MaxioTestData.Settings(s => s.PaymentCollectionMethod = "automatic"),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        GivenCustomerExists();
        _client.ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MaxioSubscription>());
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Subscription());

        await service.SubscribeAsync(Request());

        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.PaymentCollectionMethod == "automatic"),
            Arg.Any<CancellationToken>());
    }

    private void GivenCustomerExists() =>
        _client.FindCustomerByReferenceAsync(MaxioTestData.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(MaxioTestData.Customer());
}

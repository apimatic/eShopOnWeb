using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionServiceTests
{
    private const string ProPlan = "eshop-pro";
    private const string BasicPlan = "basic-plan";
    private const string UserKey = "demouser@microsoft.com";

    private readonly IBillingGateway _gateway = Substitute.For<IBillingGateway>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _gateway.ProductFamilyHandle.Returns("eshop-subscribe");
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<SubscriptionPlan>)new List<SubscriptionPlan>
            {
                new() { Handle = BasicPlan, Name = "Basic Plan", PriceInCents = 2900 },
                new() { Handle = ProPlan, Name = "Pro Plan", PriceInCents = 29900 }
            });
        _gateway.EnsureCustomerAsync(Arg.Any<SubscriberProfile>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = $"eshop:{UserKey}" });
        _gateway.ListSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>());

        _service = new SubscriptionService(_gateway, new KeyedAsyncLock(), new NoOpLogger<SubscriptionService>());
    }

    [Fact]
    public async Task CreatesSubscriptionWhenShopperHasNone()
    {
        _gateway.CreateSubscriptionAsync(42, ProPlan, UserKey, ProPlan, Arg.Any<CancellationToken>())
            .Returns(Subscription(1, SubscriptionStates.Active, ProPlan));

        var result = await _service.SubscribeAsync(Request(ProPlan));

        Assert.True(result.Created);
        Assert.Equal(1, result.Subscription.Id);
        await _gateway.Received(1).CreateSubscriptionAsync(42, ProPlan, UserKey, ProPlan, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesConfiguredDefaultPlanWhenRequestNamesNone()
    {
        _gateway.DefaultPlanHandle.Returns(ProPlan);
        _gateway.CreateSubscriptionAsync(42, ProPlan, UserKey, ProPlan, Arg.Any<CancellationToken>())
            .Returns(Subscription(7, SubscriptionStates.Active, ProPlan));

        var result = await _service.SubscribeAsync(Request(planHandle: null));

        Assert.True(result.Created);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
    }

    [Fact]
    public async Task RejectsRequestWithNoPlanAndNoDefault()
    {
        await Assert.ThrowsAsync<PlanNotSpecifiedException>(() => _service.SubscribeAsync(Request(planHandle: null)));
        AssertNothingCreated();
    }

    [Fact]
    public async Task RejectsPlanThatIsNotInTheProductFamily()
    {
        var exception = await Assert.ThrowsAsync<PlanNotFoundException>(() => _service.SubscribeAsync(Request("enterprise")));

        Assert.Equal("enterprise", exception.PlanHandle);
        AssertNothingCreated();
    }

    [Fact]
    public async Task ReturnsExistingSubscriptionFoundByReferenceInsteadOfCreatingAnother()
    {
        _gateway.FindSubscriptionAsync(UserKey, ProPlan, Arg.Any<CancellationToken>())
            .Returns(Subscription(11, SubscriptionStates.Active, ProPlan));

        var result = await _service.SubscribeAsync(Request(ProPlan));

        Assert.False(result.Created);
        Assert.Equal(11, result.Subscription.Id);
        AssertNothingCreated();
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionForTheSamePlanEvenUnderADifferentIdempotencyKey()
    {
        _gateway.ListSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CustomerSubscription>)new[] { Subscription(12, SubscriptionStates.PastDue, ProPlan) });

        var result = await _service.SubscribeAsync(Request(ProPlan, idempotencyKey: "checkout-99"));

        Assert.False(result.Created);
        Assert.Equal(12, result.Subscription.Id);
        AssertNothingCreated();
    }

    [Fact]
    public async Task IgnoresSubscriptionsToOtherPlansWhenDecidingWhetherToCreate()
    {
        _gateway.ListSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CustomerSubscription>)new[] { Subscription(13, SubscriptionStates.Active, BasicPlan) });
        _gateway.CreateSubscriptionAsync(42, ProPlan, UserKey, ProPlan, Arg.Any<CancellationToken>())
            .Returns(Subscription(14, SubscriptionStates.Active, ProPlan));

        var result = await _service.SubscribeAsync(Request(ProPlan));

        Assert.True(result.Created);
        Assert.Equal(14, result.Subscription.Id);
    }

    [Fact]
    public async Task ResubscribesAfterCancellationUnderAFreshReference()
    {
        _gateway.FindSubscriptionAsync(UserKey, ProPlan, Arg.Any<CancellationToken>())
            .Returns(Subscription(15, SubscriptionStates.Canceled, ProPlan));
        _gateway.CreateSubscriptionAsync(42, ProPlan, UserKey, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(16, SubscriptionStates.Active, ProPlan));

        var result = await _service.SubscribeAsync(Request(ProPlan));

        Assert.True(result.Created);
        Assert.Equal(16, result.Subscription.Id);

        // The canceled subscription still owns the derived reference, so the new one must not reuse it.
        var reference = (string)_gateway.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IBillingGateway.CreateSubscriptionAsync))
            .GetArguments()[3]!;
        Assert.StartsWith(ProPlan, reference, StringComparison.Ordinal);
        Assert.NotEqual(ProPlan, reference);
    }

    [Fact]
    public async Task ReplaysTerminalSubscriptionWhenTheCallerSuppliedAnExplicitIdempotencyKey()
    {
        _gateway.FindSubscriptionAsync(UserKey, "checkout-42", Arg.Any<CancellationToken>())
            .Returns(Subscription(17, SubscriptionStates.Canceled, ProPlan));

        var result = await _service.SubscribeAsync(Request(ProPlan, idempotencyKey: "checkout-42"));

        Assert.False(result.Created);
        Assert.Equal(17, result.Subscription.Id);
        AssertNothingCreated();
    }

    [Fact]
    public async Task ConcurrentSubscribesCreateExactlyOneSubscription()
    {
        // A hand-written fake rather than a mock: this exercises real concurrency, and the point of
        // the test is that the shopper cannot end up enrolled twice by a double-clicked button.
        var gateway = new InMemoryBillingGateway(BasicPlan, ProPlan);
        var service = new SubscriptionService(gateway, new KeyedAsyncLock(), new NoOpLogger<SubscriptionService>());

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => service.SubscribeAsync(Request(ProPlan)))));

        Assert.Equal(1, gateway.CreateCallCount);
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Single(results.Select(r => r.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task ReportsNoSubscriptionsWhenTheShopperHasNoBillingCustomer()
    {
        _gateway.FindCustomerAsync(UserKey, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var result = await _service.GetSubscriptionsAsync(UserKey);

        Assert.Null(result.Customer);
        Assert.Empty(result.Subscriptions);
        await _gateway.DidNotReceiveWithAnyArgs().ListSubscriptionsAsync(default);
    }

    [Fact]
    public async Task ReturnsSubscriptionsNewestFirst()
    {
        _gateway.FindCustomerAsync(UserKey, Arg.Any<CancellationToken>()).Returns(new BillingCustomer { Id = 42 });
        _gateway.ListSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CustomerSubscription>)new[]
            {
                Subscription(1, SubscriptionStates.Canceled, BasicPlan),
                Subscription(2, SubscriptionStates.Active, ProPlan)
            });

        var result = await _service.GetSubscriptionsAsync(UserKey);

        Assert.Equal(new[] { 2, 1 }, result.Subscriptions.Select(s => s.Id));
    }

    private void AssertNothingCreated() =>
        Assert.DoesNotContain(_gateway.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IBillingGateway.CreateSubscriptionAsync));

    private static SubscribeRequest Request(string? planHandle, string? idempotencyKey = null) =>
        new(new SubscriberProfile(UserKey, UserKey), planHandle, idempotencyKey);

    private static CustomerSubscription Subscription(int id, string state, string planHandle) => new()
    {
        Id = id,
        State = state,
        PlanHandle = planHandle,
        CreatedAt = DateTimeOffset.UnixEpoch.AddDays(id)
    };

    private sealed class NoOpLogger<T> : IAppLogger<T>
    {
        public void LogInformation(string message, params object[] args)
        {
        }

        public void LogWarning(string message, params object[] args)
        {
        }
    }

    /// <summary>
    /// A minimal, thread-safe stand-in for the billing provider, so concurrency can be tested for real.
    /// </summary>
    private sealed class InMemoryBillingGateway : IBillingGateway
    {
        private readonly IReadOnlyList<SubscriptionPlan> _plans;
        private readonly Dictionary<string, CustomerSubscription> _byReference = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();
        private int _nextId = 100;
        private int _createCallCount;

        public InMemoryBillingGateway(params string[] planHandles)
        {
            _plans = planHandles.Select(h => new SubscriptionPlan { Handle = h, Name = h }).ToList();
        }

        public int CreateCallCount => Volatile.Read(ref _createCallCount);

        public string ProductFamilyHandle => "eshop-subscribe";

        public string? DefaultPlanHandle => null;

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_plans);

        public Task<BillingCustomer?> FindCustomerAsync(string userKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<BillingCustomer?>(new BillingCustomer { Id = 42 });

        public Task<BillingCustomer> EnsureCustomerAsync(SubscriberProfile subscriber, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingCustomer { Id = 42 });

        public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                return Task.FromResult<IReadOnlyList<CustomerSubscription>>(_byReference.Values.ToList());
            }
        }

        public Task<CustomerSubscription?> FindSubscriptionAsync(string userKey, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _byReference.TryGetValue($"{userKey}:{idempotencyKey}", out var found);
                return Task.FromResult(found);
            }
        }

        public Task<CustomerSubscription> CreateSubscriptionAsync(
            int customerId,
            string planHandle,
            string userKey,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCallCount);
            lock (_syncRoot)
            {
                var subscription = new CustomerSubscription
                {
                    Id = _nextId++,
                    State = SubscriptionStates.Active,
                    PlanHandle = planHandle,
                    Reference = $"{userKey}:{idempotencyKey}"
                };
                _byReference[subscription.Reference] = subscription;
                return Task.FromResult(subscription);
            }
        }
    }
}

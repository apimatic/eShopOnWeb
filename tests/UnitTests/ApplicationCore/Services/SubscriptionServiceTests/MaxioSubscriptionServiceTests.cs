using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;
using Microsoft.eShopWeb.Infrastructure.Services.MaxioBilling;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class MaxioSubscriptionServiceTests
{
    private static readonly string UserId = "427db43b-4e06-42e3-aa41-3873cdd3af20";
    private const string Email = "demouser@microsoft.com";
    private const string PlanHandle = "eshop-pro";

    private readonly IRepository<Subscription> _mockRepo = Substitute.For<IRepository<Subscription>>();
    private readonly IMaxioBillingClient _mockMaxio = Substitute.For<IMaxioBillingClient>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly MaxioSubscriptionService _service;

    public MaxioSubscriptionServiceTests()
    {
        _service = new MaxioSubscriptionService(
            _mockRepo, _mockMaxio, _settings, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionService>.Instance);

        _mockMaxio.ListProductsForFamilyAsync(Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new(7130995, PlanHandle, "Pro Plan", 29900, 1, "month", "eshop-subscribe"),
            new(7130996, "basic-plan", "Basic Plan", 2900, 1, "month", "eshop-subscribe")
        });
    }

    private static MaxioSubscription FakeMaxioSubscription(string reference) =>
        new(94680110, reference, "active", PlanHandle, "Pro Plan", 29900, "USD", 1, "month",
            new DateTime(2026, 11, 6, 14, 50, 43, DateTimeKind.Utc), 99263557);

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ReturnsNotFound()
    {
        var result = await _service.SubscribeAsync(UserId, Email, "no-such-plan");

        Assert.Equal(ResultStatus.NotFound, result.Status);
        await _mockMaxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_AlreadySubscribedLocally_ReturnsExistingWithoutCallingMaxio()
    {
        var existing = new Subscription(UserId, PlanHandle, UserId, 99263557,
            $"{UserId}:{PlanHandle}", 94680110, "Pro Plan", "active", 29900, "USD", 1, "month",
            new DateTime(2026, 11, 6, 14, 50, 43, DateTimeKind.Utc));

        _mockRepo.ListAsync(Arg.Any<ISpecification<Subscription>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Subscription> { existing });

        var result = await _service.SubscribeAsync(UserId, Email, PlanHandle);

        Assert.True(result.IsSuccess);
        Assert.Equal(94680110, result.Value.MaxioSubscriptionId);
        await _mockMaxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>(), Arg.Any<CancellationToken>());
        await _mockMaxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCustomerCreate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_NewSubscription_CreatesCustomerAndSubscriptionAndPersists()
    {
        _mockRepo.ListAsync(Arg.Any<ISpecification<Subscription>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        _mockMaxio.ListSubscriptionsByCustomerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _mockMaxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _mockMaxio.CreateCustomerAsync(Arg.Any<MaxioCustomerCreate>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(99263557, Email, "Demouser", "Subscriber", "eShopOnWeb", UserId));
        _mockMaxio.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>(), Arg.Any<CancellationToken>())
            .Returns(FakeMaxioSubscription($"{UserId}:{PlanHandle}"));
        _mockRepo.AddAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Subscription>());

        var result = await _service.SubscribeAsync(UserId, Email, PlanHandle);

        Assert.True(result.IsSuccess);
        Assert.Equal(94680110, result.Value.MaxioSubscriptionId);
        await _mockRepo.Received().AddAsync(
            Arg.Is<Subscription>(s => s.UserId == UserId && s.PlanHandle == PlanHandle && s.State == "active"),
            Arg.Any<CancellationToken>());
        await _mockMaxio.Received().CreateSubscriptionAsync(
            Arg.Is<MaxioSubscriptionCreate>(c => c.ProductHandle == PlanHandle && c.CustomerId == 99263557),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_MaxioReportsDuplicateReference_FetchesExistingSubscription()
    {
        _mockRepo.ListAsync(Arg.Any<ISpecification<Subscription>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Subscription>());
        _mockMaxio.ListSubscriptionsByCustomerAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>(),
                     new List<MaxioSubscription> { FakeMaxioSubscription($"{UserId}:{PlanHandle}") });
        _mockMaxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer(99263557, Email, "Demouser", "Subscriber", "eShopOnWeb", UserId));
        _mockMaxio.CreateSubscriptionAsync(Arg.Any<MaxioSubscriptionCreate>(), Arg.Any<CancellationToken>())
            .Returns((Func<CallInfo, MaxioSubscription>)(_ => throw new MaxioApiException(422, new[] { "Reference: must be unique - that value has been taken." })));
        _mockRepo.AddAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Subscription>());

        var result = await _service.SubscribeAsync(UserId, Email, PlanHandle);

        Assert.True(result.IsSuccess);
        Assert.Equal(94680110, result.Value.MaxioSubscriptionId);
        await _mockRepo.Received().AddAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsPlansSortedByPrice()
    {
        var plans = await _service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans.First().Handle);
        Assert.Equal(29900, plans.Last().PriceInCents);
    }
}
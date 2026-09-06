using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingGatewayTests
{
    private readonly IMaxioApiClient _client = Substitute.For<IMaxioApiClient>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioBillingGateway CreateGateway() => new(
        _client,
        new MaxioSiteCache(),
        new StaticOptionsMonitor<MaxioSettings>(_settings),
        NullLogger<MaxioBillingGateway>.Instance);

    private void GivenSite(string currency = "USD", bool relationshipInvoicing = true) =>
        _client.ReadSiteAsync(Arg.Any<CancellationToken>()).Returns(new MaxioSite
        {
            Id = 1,
            Currency = currency,
            RelationshipInvoicingEnabled = relationshipInvoicing
        });

    [Theory]
    [InlineData("active", SubscriptionState.Active, true)]
    [InlineData("trialing", SubscriptionState.Trialing, true)]
    [InlineData("past_due", SubscriptionState.PastDue, true)]
    [InlineData("on_hold", SubscriptionState.OnHold, true)]
    [InlineData("awaiting_signup", SubscriptionState.AwaitingSignup, true)]
    [InlineData("canceled", SubscriptionState.Canceled, false)]
    [InlineData("expired", SubscriptionState.Expired, false)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded, false)]
    [InlineData("failed_to_create", SubscriptionState.FailedToCreate, false)]
    public void MapsEverySubscriptionStateInTheSpecification(string raw, SubscriptionState expected, bool isCurrent)
    {
        var state = MaxioSubscriptionStates.Parse(raw);

        Assert.Equal(expected, state);
        Assert.Equal(isCurrent, state.IsCurrent());
    }

    [Fact]
    public void TreatsAnUnrecognisedStateAsStillHeldSoNoDuplicateIsCreated()
    {
        var state = MaxioSubscriptionStates.Parse("a_state_from_the_future");

        Assert.Equal(SubscriptionState.Unknown, state);
        Assert.True(state.IsCurrent());
    }

    [Fact]
    public async Task ListsOnlyNonArchivedPlansPricedInTheSiteCurrency()
    {
        GivenSite("EUR");
        _client.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new MaxioProduct { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new MaxioProduct { Handle = "basic-plan", Name = "Basic", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
            new MaxioProduct { Handle = "retired", Name = "Retired", ArchivedAt = DateTimeOffset.UtcNow }
        });

        var plans = await CreateGateway().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle).ToArray());
        Assert.All(plans, p => Assert.Equal("EUR", p.Currency));
        Assert.Equal(29m, plans[0].Price);
    }

    [Fact]
    public async Task SkipsProductsWithoutAHandleBecauseTheyCannotBeSubscribedTo()
    {
        GivenSite();
        _client.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new MaxioProduct { Handle = null, Name = "No handle", PriceInCents = 100 }
        });

        Assert.Empty(await CreateGateway().ListPlansAsync());
    }

    [Fact]
    public async Task ReadsTheSiteOnlyOnceWhileTheCacheIsWarm()
    {
        GivenSite();
        var gateway = CreateGateway();

        await gateway.GetSiteAsync();
        await gateway.GetSiteAsync();

        await _client.Received(1).ReadSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCacheTheSiteWhenTheLifetimeIsZero()
    {
        _settings.SiteCacheMinutes = 0;
        GivenSite();
        var gateway = CreateGateway();

        await gateway.GetSiteAsync();
        await gateway.GetSiteAsync();

        await _client.Received(2).ReadSiteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsRejectedCredentialsAsAConfigurationFault()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpMethod.Get, "site.json", HttpStatusCode.Unauthorized, Array.Empty<string>()));

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateGateway().GetSiteAsync());
    }

    [Fact]
    public async Task ReportsAMissingProductFamilyAsAConfigurationFault()
    {
        GivenSite();
        _client.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpMethod.Get, "products.json", HttpStatusCode.NotFound,
                new[] { "A valid product_family_id is required" }));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateGateway().ListPlansAsync());

        Assert.Contains("A valid product_family_id is required", exception.Errors);
    }

    [Fact]
    public async Task ReportsAValidationFailureAsARejectedRequest()
    {
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpMethod.Post, "subscriptions.json", HttpStatusCode.UnprocessableEntity,
                new[] { "No payment method was on file" }));

        var exception = await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => CreateGateway().CreateSubscriptionAsync(new NewSubscription(1, "eshop-pro", "ref", null)));

        Assert.Contains("No payment method was on file", exception.Errors);
    }

    [Fact]
    public async Task ReportsAServerFailureAsProviderUnavailable()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException(HttpMethod.Get, "site.json", HttpStatusCode.InternalServerError, Array.Empty<string>()));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateGateway().GetSiteAsync());
    }

    [Fact]
    public async Task ReportsATransportFailureAsProviderUnavailable()
    {
        _client.ReadSiteAsync(Arg.Any<CancellationToken>())
            .Throws(new MaxioTransportException(HttpMethod.Get, "site.json", "the request timed out."));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => CreateGateway().GetSiteAsync());
    }

    [Fact]
    public async Task FailsFastWhenNoProductFamilyIsConfigured()
    {
        _settings.ProductFamilyHandle = string.Empty;

        await Assert.ThrowsAsync<BillingConfigurationException>(() => CreateGateway().ListPlansAsync());
    }

    [Fact]
    public async Task FallsBackToTheEndOfTheCurrentPeriodWhenNoAssessmentIsScheduled()
    {
        var periodEnd = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        _client.FindSubscriptionAsync("ref", Arg.Any<CancellationToken>()).Returns(new MaxioSubscription
        {
            Id = 1,
            State = "awaiting_signup",
            CurrentPeriodEndsAt = periodEnd,
            NextAssessmentAt = null
        });

        var subscription = await CreateGateway().FindSubscriptionByReferenceAsync("ref");

        Assert.Equal(periodEnd, subscription!.NextBillingAt);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

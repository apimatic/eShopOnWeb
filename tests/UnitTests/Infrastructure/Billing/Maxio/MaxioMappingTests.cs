using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioMappingTests
{
    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Active)]
    [InlineData("pending", SubscriptionState.Pending)]
    [InlineData("awaiting_signup", SubscriptionState.Pending)]
    [InlineData("past_due", SubscriptionState.ProblemState)]
    [InlineData("unpaid", SubscriptionState.ProblemState)]
    [InlineData("canceled", SubscriptionState.Ended)]
    [InlineData("expired", SubscriptionState.Ended)]
    [InlineData("trial_ended", SubscriptionState.Ended)]
    [InlineData("something_new", SubscriptionState.Unknown)]
    [InlineData(null, SubscriptionState.Unknown)]
    public void ToState_BucketsProviderStates(string? providerState, SubscriptionState expected) =>
        Assert.Equal(expected, MaxioSubscriptionMapper.ToState(providerState));

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Pending, true)]
    [InlineData(SubscriptionState.ProblemState, true)]
    [InlineData(SubscriptionState.Unknown, true)]
    [InlineData(SubscriptionState.Ended, false)]
    public void OccupiesPlan_TreatsAnythingButEndedAsStillHoldingThePlan(
        SubscriptionState state, bool expected) =>
        Assert.Equal(expected, MaxioSubscriptionMapper.OccupiesPlan(state));

    [Fact]
    public void ToSubscription_PrefersNextAssessmentOverThePeriodEnd()
    {
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30);
        var retryAt = DateTimeOffset.UtcNow.AddHours(24);

        var mapped = MaxioSubscriptionMapper.ToSubscription(
            new MaxioSubscription
            {
                Id = 1,
                State = "past_due",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = periodEnd,
                NextAssessmentAt = retryAt,
                CreatedAt = DateTimeOffset.UtcNow,
                Product = MaxioTestBuilder.Product("eshop-pro", "Pro Plan", 29900)
            },
            "USD",
            "eshoponweb:someone@example.com",
            plan: null);

        Assert.Equal(retryAt, mapped.NextBillingAt);
        Assert.Equal(SubscriptionState.ProblemState, mapped.State);
        Assert.Equal(299m, mapped.Price);
    }

    [Fact]
    public void ToSubscription_OnAnEndedSubscription_ReportsNoNextBillingDate()
    {
        var mapped = MaxioSubscriptionMapper.ToSubscription(
            new MaxioSubscription
            {
                Id = 2,
                State = "canceled",
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(10),
                NextAssessmentAt = DateTimeOffset.UtcNow.AddDays(10),
                CanceledAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-1),
                Product = MaxioTestBuilder.Product("eshop-pro", "Pro Plan", 29900)
            },
            "USD",
            "eshoponweb:someone@example.com",
            plan: null);

        Assert.Null(mapped.NextBillingAt);
    }

    [Theory]
    [InlineData("jane.doe@contoso.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@contoso.com", "Jane", "Van Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Microsoft")]
    [InlineData("solo", "Solo", "Customer")]
    public void DeriveName_ProducesANameMaxioWillAccept(string email, string first, string last)
    {
        var (derivedFirst, derivedLast) = MaxioCustomerMapping.DeriveName(email);

        Assert.Equal(first, derivedFirst);
        Assert.Equal(last, derivedLast);
        Assert.False(string.IsNullOrWhiteSpace(derivedLast));
    }

    [Fact]
    public void ToCustomerAttributes_PrefersACallerSuppliedName()
    {
        var attributes = MaxioCustomerMapping.ToCustomerAttributes(
            new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com", "Ada", "Lovelace"),
            "eshoponweb:demouser@microsoft.com");

        Assert.Equal("Ada", attributes.FirstName);
        Assert.Equal("Lovelace", attributes.LastName);
        Assert.Equal("eshoponweb:demouser@microsoft.com", attributes.Reference);
    }

    [Fact]
    public void CustomerReference_IsCaseInsensitiveAndNamespaced()
    {
        var lower = MaxioCustomerMapping.CustomerReference("eshoponweb", new SubscriberIdentity("demo@x.com"));
        var upper = MaxioCustomerMapping.CustomerReference("eshoponweb", new SubscriberIdentity(" DEMO@X.com "));

        Assert.Equal("eshoponweb:demo@x.com", lower);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void UniquenessToken_VariesByPlanGenerationAndCallerKey()
    {
        var baseline = MaxioCustomerMapping.UniquenessToken("ref", "eshop-pro", 0, null);

        Assert.Equal(baseline, MaxioCustomerMapping.UniquenessToken("ref", "eshop-pro", 0, null));
        Assert.NotEqual(baseline, MaxioCustomerMapping.UniquenessToken("ref", "basic-plan", 0, null));
        Assert.NotEqual(baseline, MaxioCustomerMapping.UniquenessToken("ref", "eshop-pro", 1, null));
        Assert.NotEqual(baseline, MaxioCustomerMapping.UniquenessToken("ref", "eshop-pro", 0, "caller-key"));
        Assert.NotEqual(baseline, MaxioCustomerMapping.UniquenessToken("other", "eshop-pro", 0, null));
    }
}

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromTheSubdomain()
    {
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "f" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_UsesTheOverrideWhenSupplied()
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            Subdomain = "acme",
            ProductFamilyHandle = "f",
            BaseUrl = "https://billing.internal.example/v1"
        };

        Assert.Equal(new Uri("https://billing.internal.example/v1/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_WorksFromTheOverrideAloneWithNoSubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "f",
            BaseUrl = "https://stub.local/"
        };

        options.EnsureConfigured();
        Assert.Equal(new Uri("https://stub.local/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void EnsureConfigured_NamesEveryMissingKeyAtOnce()
    {
        var exception = Assert.Throws<BillingNotConfiguredException>(() => new MaxioOptions().EnsureConfigured());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void EnsureConfigured_DoesNotEchoTheApiKey()
    {
        var options = new MaxioOptions { ApiKey = "super-secret-key" };

        var exception = Assert.Throws<BillingNotConfiguredException>(() => options.EnsureConfigured());

        Assert.DoesNotContain("super-secret-key", exception.Message);
    }
}

public class MaxioApiClientErrorParsingTests
{
    [Fact]
    public void ParseErrors_ReadsTheArrayForm()
    {
        var errors = MaxioApiClient.ParseErrors("{\"errors\":[\"Last name: cannot be blank.\"]}");

        Assert.Equal(new[] { "Last name: cannot be blank." }, errors);
    }

    [Fact]
    public void ParseErrors_ReadsTheFieldKeyedFormAndKeepsTheFieldName()
    {
        var errors = MaxioApiClient.ParseErrors("{\"errors\":{\"customer\":\"has already been taken\"}}");

        Assert.Equal(new[] { "customer: has already been taken" }, errors);
    }

    [Fact]
    public void ParseErrors_FallsBackToTheRawBodyWhenItIsNotJson()
    {
        var errors = MaxioApiClient.ParseErrors("<html>gateway timeout</html>");

        Assert.Equal(new[] { "<html>gateway timeout</html>" }, errors);
    }

    [Fact]
    public void ParseErrors_OnAnEmptyBody_IsEmpty() => Assert.Empty(MaxioApiClient.ParseErrors(""));

    [Fact]
    public void MaxioApiException_RecognisesDuplicateSubmission()
    {
        var exception = new MaxioApiException(
            System.Net.Http.HttpMethod.Post,
            "subscriptions.json",
            System.Net.HttpStatusCode.Conflict,
            new[] { "DuplicatePrevention::DuplicateSubmissionError" });

        Assert.True(exception.IsDuplicateSubmission);
        Assert.False(exception.IsReferenceTaken);
    }

    [Fact]
    public void MaxioApiException_RecognisesATakenReference()
    {
        var exception = new MaxioApiException(
            System.Net.Http.HttpMethod.Post,
            "customers.json",
            System.Net.HttpStatusCode.UnprocessableEntity,
            new[] { "reference: has already been taken" });

        Assert.True(exception.IsReferenceTaken);
        Assert.False(exception.IsDuplicateSubmission);
    }
}

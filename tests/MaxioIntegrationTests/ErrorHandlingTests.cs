using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Every provider failure has to arrive as a typed billing exception whose message is safe to show a
/// caller — no SDK type, no parser offset, no raw payload, no credential.
/// </summary>
public class ErrorHandlingTests
{
    [Fact]
    public async Task AProviderValidationFailureBecomesARejectionCarryingTheProvidersMessages()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", BillingPayloads.ValidationErrors,
                HttpStatusCode.UnprocessableEntity);
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => client.CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Equal("CreateSubscriptionAsync", exception.Operation);
        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.Contains("Product must be provided", exception.ProviderErrors);
        Assert.Contains("Subscription is not active", exception.ProviderErrors);
        Assert.Contains("Product must be provided", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerSideFailureBecomesAnUnavailableProviderNotARejection()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions.json", """{"error":"boom"}""",
                HttpStatusCode.InternalServerError);
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.CreateSubscriptionAsync(88001, "eshop-pro"));

        Assert.Equal(500, exception.ProviderStatusCode);
        Assert.IsNotType<BillingRequestRejectedException>(exception);
    }

    [Fact]
    public async Task AMissingRecordOnAWriteBecomesANotFoundFailure()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Post, "/subscriptions/15236915/hold.json", "{}", HttpStatusCode.NotFound);
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingEntityNotFoundException>(
            () => client.PauseSubscriptionAsync(15236915));

        Assert.Equal(404, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task RefusedCredentialsAreReportedWithoutEchoingAnythingCredentialShaped()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json",
                $$"""{"errors":["invalid api key {{BillingClientFixture.ApiKey}}"]}""",
                HttpStatusCode.Unauthorized);
        var (client, logger) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.ListPlansAsync());

        Assert.Equal(401, exception.ProviderStatusCode);
        Assert.DoesNotContain(BillingClientFixture.ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(BillingClientFixture.ApiKey, logger.AllText, StringComparison.Ordinal);
        Assert.Empty(exception.ProviderErrors);
    }

    [Fact]
    public async Task AnUnreadablePayloadIsAProviderFaultAndLeaksNoParserDetail()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", """{"product_family": not-json}""");
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.ListPlansAsync());

        Assert.DoesNotContain("BytePositionInLine", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxioAdvancedBilling", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APayloadOfTheWrongShapeIsAProviderFaultToo()
    {
        var provider = new FakeBillingProvider()
            .Respond(HttpMethod.Get, "/product_families.json", """{"unexpected":"object instead of a list"}""");
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.ListPlansAsync());

        Assert.DoesNotContain("IReadOnlyList", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableProviderIsReportedWithoutTheTransportDetail()
    {
        var provider = new UnreachableProvider();
        var (client, _) = BillingClientFixture.Create(provider);

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.ListPlansAsync());

        Assert.Contains("could not be reached", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("socket", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AProviderThatNeverAnswersIsCutOffAtTheConfiguredTimeout()
    {
        var settings = BillingClientFixture.Settings();
        settings.TimeoutSeconds = 1;

        var provider = new FakeBillingProvider()
            .RespondSlowly(HttpMethod.Get, "/product_families.json", TimeSpan.FromSeconds(20));
        var (client, _) = BillingClientFixture.Create(provider, settings);

        var started = DateTimeOffset.UtcNow;
        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => client.ListPlansAsync());

        Assert.Contains("within 1 seconds", exception.Message, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10),
            "the call must be abandoned at the configured timeout, not left hanging");
    }

    [Fact]
    public async Task ACallerWhoCancelsGetsCancellationNotAFabricatedProviderFailure()
    {
        var provider = new FakeBillingProvider()
            .RespondSlowly(HttpMethod.Get, "/product_families.json", TimeSpan.FromSeconds(20));
        var (client, _) = BillingClientFixture.Create(provider);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListPlansAsync(cancellation.Token));
    }

    [Fact]
    public async Task AMissingApiKeyIsAConfigurationFaultRaisedBeforeAnythingIsSent()
    {
        var settings = BillingClientFixture.Settings();
        settings.ApiKey = null;

        var provider = new FakeBillingProvider().WithCatalog();
        var (client, _) = BillingClientFixture.Create(provider, settings);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.Requests);
    }

    private sealed class UnreachableProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it (socket 10061)");
    }
}

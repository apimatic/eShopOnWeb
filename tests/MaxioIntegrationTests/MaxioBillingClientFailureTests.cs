using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Failure paths: every provider or transport problem must surface as the typed
/// <see cref="BillingProviderException"/>, and no credential may ever ride along with it.
/// </summary>
public class MaxioBillingClientFailureTests
{
    [Fact]
    public async Task ARejectedWriteSurfacesAsATypedExceptionCarryingTheProvidersStatusAndMessages()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.ValidationErrors);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Product: is invalid.", exception.ProviderErrors);
        Assert.Contains("Quantity: must be positive.", exception.ProviderErrors);
    }

    [Fact]
    public async Task FieldKeyedProviderErrorsAreReadAsWell()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.UnprocessableEntity, MaxioPayloads.CustomerReferenceTaken);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro"));

        Assert.Contains("reference: has already been taken", exception.ProviderErrors);
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsATypedExceptionWithNoStatus()
    {
        var server = new FakeMaxioServer()
            .Fail(HttpMethod.Post, "subscriptions.json", new HttpRequestException("No such host is known (billing.test:443)"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro"));

        Assert.Null(exception.StatusCode);
        // The transport message names the outbound host, so it stays in the inner exception.
        Assert.DoesNotContain("billing.test", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task AProviderErrorOnARequiredReadIsNotSwallowedAsAnEmptyResult()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).GetSubscriptionAsync(15236915));
    }

    [Fact]
    public async Task AnUnreadablePayloadSurfacesAsATypedExceptionRatherThanAJsonException()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "subscriptions/15236915.json", HttpStatusCode.OK, "<html>gateway error</html>");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).GetSubscriptionAsync(15236915));

        Assert.Equal(200, exception.StatusCode);
    }

    [Fact]
    public async Task ASuccessfulResponseWithNoSubscriptionIsTreatedAsAProviderFailure()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Created, "{}");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro"));
    }

    [Fact]
    public async Task AMissingApiKeyFailsBeforeAnythingIsSentToTheProvider()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "products", MaxioPayloads.ProductList);

        var client = BillingClientFactory.Create(server, settings => settings.ApiKey = null);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task EveryCallCarriesTheApiKeyAsBasicAuthWithMaxiosLiteralXPassword()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Get, "product_families", MaxioPayloads.ProductList);

        await BillingClientFactory.Create(server).ListPlansAsync();

        var authorization = Assert.Single(server.Requests).Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal($"{BillingClientFactory.ApiKey}:x",
            Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task AProviderThatEchoesTheApiKeyBackNeverHasItRelayedOrLogged()
    {
        var leakedBody = $$"""{"errors":["Invalid credentials supplied: {{BillingClientFactory.ApiKey}}"]}""";
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.Unauthorized, leakedBody);
        var logger = new RecordingAppLogger<Infrastructure.Services.MaxioBillingClient>();

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server, logger: logger).CreateSubscriptionAsync(14714298, "eshop-pro"));

        Assert.DoesNotContain(BillingClientFactory.ApiKey, exception.Message);
        Assert.DoesNotContain(BillingClientFactory.ApiKey, string.Join('\n', exception.ProviderErrors));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(BillingClientFactory.ApiKey));
        Assert.Contains("[REDACTED]", string.Join('\n', exception.ProviderErrors));
    }

    [Fact]
    public async Task ANonJsonErrorBodyIsNeverEchoedBackAsAProviderMessage()
    {
        var server = new FakeMaxioServer()
            .Respond(HttpMethod.Post, "subscriptions.json", HttpStatusCode.BadGateway,
                "<html><body>nginx/1.24.0 at 10.0.0.7</body></html>");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFactory.Create(server).CreateSubscriptionAsync(14714298, "eshop-pro"));

        Assert.Empty(exception.ProviderErrors);
        Assert.DoesNotContain("nginx", exception.Message);
        Assert.Equal(502, exception.StatusCode);
    }
}

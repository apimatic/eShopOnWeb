using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class LifecycleAndErrorTests
{
    [Fact]
    public async Task Pause_PostsHold_MapsOnHoldState()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "on_hold", "eshop-pro", 29900));

        var updated = await client.PauseAsync(100);

        Assert.Equal("on_hold", updated.State);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("/subscriptions/100/hold.json", handler.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task Resume_PostsResume_MapsActiveState()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "active", "eshop-pro", 29900));

        var updated = await client.ResumeAsync(100);

        Assert.Equal("active", updated.State);
        Assert.Contains("/subscriptions/100/resume.json", handler.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task Cancel_Immediate_UsesDeleteVerb()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "canceled", "eshop-pro", 29900));

        var updated = await client.CancelAsync(100, immediate: true, reason: "too expensive");

        Assert.Equal("canceled", updated.State);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Contains("/subscriptions/100.json", request.PathAndQuery);
        Assert.Contains("\"cancellation_message\":\"too expensive\"", request.Body);
    }

    [Fact]
    public async Task Cancel_EndOfPeriod_PostsDelayedCancel_ThenReReadsSubscription()
    {
        // delayed_cancel returns only a message, so the client must re-GET the subscription for state.
        var routes = new List<(string, string, HttpStatusCode, string)>
        {
            ("POST", "/subscriptions/100/delayed_cancel.json", HttpStatusCode.OK, """{ "message": "will cancel at period end" }"""),
            ("GET", "/subscriptions/100.json", HttpStatusCode.OK, SubscriptionCrudTests.SubscriptionJson(100, "active", "eshop-pro", 29900))
        };
        var (client, handler) = MaxioClientHarness.WithRoutes(routes);

        var updated = await client.CancelAsync(100, immediate: false, reason: null);

        Assert.Equal("active", updated.State);   // still active until period end
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("delayed_cancel.json", handler.Requests[0].PathAndQuery);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task Reactivate_UsesPutVerb_MapsActiveState()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK,
            SubscriptionCrudTests.SubscriptionJson(100, "active", "eshop-pro", 29900));

        var updated = await client.ReactivateAsync(100);

        Assert.Equal("active", updated.State);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Contains("/subscriptions/100/reactivate.json", request.PathAndQuery);
    }

    [Fact]
    public async Task ProviderValidationError_422_SurfacesErrorsListInException()
    {
        const string errorJson = """{ "errors": ["No credit card was on file", "This subscription is not eligible"] }""";
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.UnprocessableEntity, errorJson);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(1, "eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No credit card was on file", ex.Message);
        Assert.Contains("This subscription is not eligible", ex.Message);
    }

    [Fact]
    public async Task ProviderAuthError_401_ThrowsTypedExceptionWithStatusCode()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.Unauthorized, "");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task MeteredComponentLookup_MissingComponentEnvelope_ThrowsConfigurationException()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, "{ }");

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetMeteredComponentAsync());
    }
}

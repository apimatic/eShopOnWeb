using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Support;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Error/edge paths that are impractical or unsafe to provoke against a live sandbox (arbitrary 5xx,
/// a misconfigured component kind, a 422 validation rejection) - stubbed via the HttpClient-constructor
/// seam per the SDK's own testing convention, so these assertions are deterministic and fast.
/// </summary>
public class ErrorMappingTests
{
    [Fact]
    public async Task GetSubscriptionAsync_404_ThrowsSubscriptionNotFoundException_NotAGenericProviderError()
    {
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.NotFound, """{"error":"not found"}""", out _);

        var ex = await Assert.ThrowsAsync<SubscriptionNotFoundException>(() => client.GetSubscriptionAsync(4242));

        Assert.Contains("4242", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ServerError_ThrowsBillingProviderException_WithStatusCodePreserved()
    {
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.InternalServerError, """{"error":"boom"}""", out _);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(1));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task ListAvailablePlansAsync_UnresolvableHandle_ThrowsBillingConfigurationException_PointingBackAtUC0()
    {
        // A 404 on the configured product handle is a *configuration* problem (UC0), distinct from a
        // transient provider outage - the two must not be conflated into the same exception type.
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.NotFound, """{"error":"not found"}""", out _);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListAvailablePlansAsync());

        Assert.Contains("UC0", ex.Message);
    }

    [Fact]
    public async Task ValidateUsageComponentAsync_WrongKind_ThrowsBillingConfigurationException()
    {
        const string json = """
            { "component": { "id": 3057195, "name": "API Calls", "handle": "api-call", "kind": "quantity_based_component" } }
            """;
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.OK, json, out _);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ValidateUsageComponentAsync());

        Assert.Contains("Metered", ex.Message);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_422ValidationError_ThrowsBillingProviderException_WithProviderMessages()
    {
        const string json = """{ "errors": ["Customer can't be blank", "Product can't be blank"] }""";
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.UnprocessableEntity, json, out _);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.CreateSubscriptionAsync(1, "eshop-pro"));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Customer can't be blank", ex.Message);
        Assert.Contains("Product can't be blank", ex.Message);
    }

    [Fact]
    public async Task RecordUsageAsync_OnAMisconfiguredComponent_ThrowsBillingConfigurationException_BeforeCallingCreateUsage()
    {
        const string json = """
            { "component": { "id": 3057195, "name": "API Calls", "handle": "api-call", "kind": "on_off_component" } }
            """;
        var client = MaxioBillingClientTestFactory.CreateStubbed(HttpStatusCode.OK, json, out var handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.RecordUsageAsync(1, 1, null));

        // Only the component-validation GET should have been attempted - never a usage POST against
        // a component we already know is the wrong kind.
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }
}

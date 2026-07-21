using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class GetMeteredComponentAsyncTests
{
    [Fact]
    public async Task ReturnsIsMeteredTrueForAMeteredComponent()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "component": { "id": 3057295, "handle": "api-call", "kind": "metered_component" } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMetered);
    }

    [Fact]
    public async Task ReturnsIsMeteredFalseForANonMeteredComponent()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            { "component": { "id": 3057295, "handle": "api-call", "kind": "quantity_based_component" } }
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.False(component.IsMetered);
    }

    [Fact]
    public async Task ThrowsBillingConfigurationExceptionWhenComponentHandleDoesNotResolve()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.NotFound));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetMeteredComponentAsync());

        Assert.Contains("api-call", ex.Message);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionOnUnexpectedServerError()
    {
        var handler = new SequentialStubHandler(SequentialStubHandler.Empty(HttpStatusCode.InternalServerError));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetMeteredComponentAsync());

        Assert.Equal(500, ex.StatusCode);
    }
}

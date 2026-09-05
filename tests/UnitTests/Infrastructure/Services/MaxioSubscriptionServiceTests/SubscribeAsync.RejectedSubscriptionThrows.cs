using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsyncRejectedSubscriptionThrows
{
    [Fact]
    public async Task SurfacesTheProviderValidationMessageAsABadRequest()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Plan handle is invalid"] }"""));
        var service = MaxioTestSupport.CreateService(handler);

        var ex = await Assert.ThrowsAsync<MaxioSubscriptionException>(
            () => service.SubscribeAsync("user-1", "jane.doe@example.com", "not-a-real-plan"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Plan handle is invalid", ex.Message);
    }
}

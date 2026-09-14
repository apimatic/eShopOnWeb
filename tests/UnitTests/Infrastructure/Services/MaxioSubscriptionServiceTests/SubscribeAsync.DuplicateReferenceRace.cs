using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsyncDuplicateReferenceRace
{
    /// <summary>
    /// A double-click (or any concurrent double-submit) can make CreateCustomer 422 on the reference-
    /// uniqueness check even though the first request already created the customer. This must recover by
    /// re-reading rather than failing the second click - see maxio-plan.md §5.
    /// </summary>
    [Fact]
    public async Task RecoversTheExistingCustomerInsteadOfFailing()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": [] }"""),
            // CustomerErrorResponse1.Errors only ever carries per_page/price_point (see maxio-plan.md §5) -
            // this is the real wire shape a 422 takes, not a duplicate-reference message.
            _ => MaxioTestSupport.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "per_page": null, "price_point": null } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } }
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");

        Assert.Equal(999, subscription.Id);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task ThrowsAGenericMessageWhenTheRecheckStillFindsNoCustomer()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": [] }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "per_page": null, "price_point": null } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": [] }"""));
        var service = MaxioTestSupport.CreateService(handler);

        var ex = await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.MaxioSubscriptionException>(
            () => service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}

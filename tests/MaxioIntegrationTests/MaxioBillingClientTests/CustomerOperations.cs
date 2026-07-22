using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// The customer lookup is what makes enrolment idempotent on the eShopOnWeb user reference
/// (plan.md §4.4), so "absent" and "failed" must never be confused.
/// </summary>
public class CustomerOperations
{
    [Fact]
    public async Task FindsAnExistingCustomerByTheEShopOnWebUserReference()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK,
            MaxioJson.Customer());

        var customer = await harness.Client.FindCustomerByReferenceAsync(MaxioJson.UserReference);

        Assert.NotNull(customer);
        Assert.Equal(MaxioJson.CustomerId, customer.Id);
        Assert.Equal(MaxioJson.UserReference, customer.Reference);
        Assert.Equal("Demouser", customer.FirstName);
    }

    [Fact]
    public async Task ReturnsNullWhenTheUserHasNeverBeenEnrolled()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "{}");

        var customer = await harness.Client.FindCustomerByReferenceAsync("new-user@example.com");

        // A clean miss is not an error: the caller creates the customer next.
        Assert.Null(customer);
    }

    [Fact]
    public async Task ThrowsRatherThanReportingAbsenceWhenTheLookupItselfFails()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.Unauthorized,
            """{ "error": "bad credentials" }""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.FindCustomerByReferenceAsync(MaxioJson.UserReference));

        // Treating a 401 as "no such customer" would create a duplicate customer on every call.
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task CreatesACustomerCarryingTheUserReference()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.Created,
            MaxioJson.Customer());

        var customer = await harness.Client.CreateCustomerAsync(
            MaxioJson.UserReference, MaxioJson.UserReference, "Demouser", "eShopOnWeb");

        Assert.Equal(MaxioJson.CustomerId, customer.Id);

        var body = harness.Handler.Requests.Single().Body;

        // The reference is what makes a repeat subscribe find this customer again.
        Assert.Contains("\"reference\"", body);
        Assert.Contains(MaxioJson.UserReference, body);
        Assert.Contains("\"first_name\"", body);
        Assert.Contains("\"last_name\"", body);
    }

    [Fact]
    public async Task SurfacesATypedErrorWhenMaxioRejectsTheCustomerDetails()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
            MaxioJson.ErrorList("Email: is invalid."));

        // Maxio's real customer-validation body is a list of messages, but the SDK generated a
        // different shape for this operation's 422 and its deserializer throws on the real thing.
        // The seam must still produce its own exception type rather than leaking a JsonException.
        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateCustomerAsync("bad", "not-an-email", "A", "B"));

        Assert.IsNotType<System.Text.Json.JsonException>(exception);
        Assert.Contains("could not be read", exception.Message);
    }

    [Fact]
    public async Task NeverLetsAnUnreadableErrorBodyEscapeAsARawSerializationFailure()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
            """{ "errors": { "unexpected": ["shape"] }, "extra": 42 }""");

        var exception = await Record.ExceptionAsync(
            () => harness.Client.CreateCustomerAsync(MaxioJson.UserReference, MaxioJson.UserReference, "A", "B"));

        // Whatever Maxio sends back, callers only ever have to catch one exception type.
        Assert.IsType<BillingProviderException>(exception);
    }

    [Fact]
    public async Task SurfacesAProviderFailureWhenCustomerCreationBreaksDown()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.InternalServerError,
            "upstream exploded");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateCustomerAsync(MaxioJson.UserReference, MaxioJson.UserReference, "A", "B"));

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task ReportsANetworkFailureAsAProviderErrorRatherThanLeakingTheTransportException()
    {
        using var harness = MaxioTestHarness.Create();
        harness.Handler.Fail(HttpMethod.Post, "/customers.json",
            new HttpRequestException("no route to host"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.CreateCustomerAsync(MaxioJson.UserReference, MaxioJson.UserReference, "A", "B"));

        // Transport failures never surface as an SDK error, so the seam has to translate them too.
        Assert.Contains("could not be reached", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}

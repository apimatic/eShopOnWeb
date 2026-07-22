using System.Net;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class EnsureCustomer
{
    private const string Reference = "demouser@microsoft.com";
    private const string LookupPath = "customers/lookup.json?reference=demouser@microsoft.com";

    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task ReturnsTheExistingCustomerWithoutCreatingASecondOne()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.OK, MaxioPayloads.Customer);

        var customer = await _builder.Build()
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft.com");

        Assert.Equal(88001, customer.Id);
        Assert.Equal(Reference, customer.Reference);

        // Idempotency is the whole point: a repeated subscribe must not create a duplicate customer.
        Assert.DoesNotContain(_builder.Handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CreatesTheCustomerWhenTheReferenceIsUnknown()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, string.Empty);
        _builder.Handler.RespondWith(HttpMethod.Post, "customers.json", HttpStatusCode.OK, MaxioPayloads.Customer);

        var customer = await _builder.Build()
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft.com");

        Assert.Equal(88001, customer.Id);

        var created = Assert.Single(_builder.Handler.Requests.Where(r => r.Method == HttpMethod.Post));
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", created.Body);
        Assert.Contains("\"first_name\":\"demouser\"", created.Body);
        Assert.Contains("\"last_name\":\"microsoft.com\"", created.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", created.Body);
    }

    [Fact]
    public async Task SendsTheApiKeyAsHttpBasicWithPasswordX()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.OK, MaxioPayloads.Customer);

        await _builder.Build().EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft.com");

        var expected = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("test-api-key:x"));
        Assert.Equal($"Basic {expected}", _builder.Handler.Requests.Single().Authorization);
    }
}

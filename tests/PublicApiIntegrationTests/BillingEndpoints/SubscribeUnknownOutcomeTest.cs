using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// A subscribe whose connection fails after the request may have been sent must be settled by looking the
/// subscription up by the reference that was sent, never reported as a plain failure.
/// The handler injects the transport failure; everything else is answered like Maxio answers.
/// </summary>
[TestClass]
public class SubscribeUnknownOutcomeTest
{
    private sealed class FaultyMaxio : HttpMessageHandler
    {
        public bool SubscriptionExistsAfterFailure { get; init; }
        public string? SentReference { get; private set; }
        public int CreateCalls { get; private set; }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/products.json"))
            {
                return Json(HttpStatusCode.OK, "[{\"product\":{\"id\":1,\"name\":\"Pro\",\"handle\":\"pro\",\"price_in_cents\":1000,\"product_family\":{\"id\":9,\"name\":\"Azure\"}}}]");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, "{\"customer\":{\"id\":5,\"reference\":\"demouser@microsoft.com\"}}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/5/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
            {
                CreateCalls++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var marker = "\"reference\":\"";
                var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                SentReference = body.Substring(start, body.IndexOf('"', start) - start);
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
            {
                return SubscriptionExistsAfterFailure
                    ? Json(HttpStatusCode.OK, "{\"subscription\":{\"id\":77,\"state\":\"active\",\"balance_in_cents\":1000,\"reference\":\"" + SentReference + "\",\"product\":{\"handle\":\"pro\",\"name\":\"Pro\",\"price_in_cents\":1000},\"customer\":{\"reference\":\"demouser@microsoft.com\"}}}")
                    : Json(HttpStatusCode.NotFound, "{}");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }
    }

    private static MaxioBillingService CreateService(FaultyMaxio handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Environment = ServerEnvironment.Us
        };
        options.Server.Production.Us.BaseUrl = "http://maxio.invalid";
        options.Retry = MaxioAdvancedBilling.Core.Configuration.RetryOptions.Disabled();
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var brands = Substitute.For<IReadRepository<CatalogBrand>>();
        brands.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<CatalogBrand> { new("Azure") });
        var users = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        return new MaxioBillingService(client, brands, users, Options.Create(new MaxioSettings { CallBudgetSeconds = 20 }), NullLogger<MaxioBillingService>.Instance);
    }

    [TestMethod]
    public async Task SettlesBySentReferenceWhenTheSubscriptionWasCreated()
    {
        var handler = new FaultyMaxio { SubscriptionExistsAfterFailure = true };

        var result = await CreateService(handler).SubscribeAsync("demouser@microsoft.com", "pro", CancellationToken.None);

        Assert.AreEqual(1, handler.CreateCalls);
        Assert.AreEqual(77, result.Id);
        Assert.AreEqual("pro", result.PlanHandle);
    }

    [TestMethod]
    public async Task ReportsNotCreatedWhenTheLookupFindsNothing()
    {
        var handler = new FaultyMaxio { SubscriptionExistsAfterFailure = false };

        var ex = await Assert.ThrowsExceptionAsync<BillingException>(() =>
            CreateService(handler).SubscribeAsync("demouser@microsoft.com", "pro", CancellationToken.None));

        Assert.AreEqual(502, ex.StatusCode);
        Assert.AreEqual(1, handler.CreateCalls);
    }
}

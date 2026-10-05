using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The seam for these tests is the HttpClient the SDK client is built over (see
/// dotnet-testing): a stub handler fakes Maxio without any network. They assert the
/// unknown-outcome path: when the connection fails on the CreateSubscription write,
/// the flow settles the outcome by re-reading the deterministic reference.
/// </summary>
[TestClass]
public class MaxioBillingUnknownOutcomeTest
{
    private const string PlanHandle = "pro-plan";
    private const string UserId = "u1";
    private const string Reference = "eshopweb:u1:pro-plan";

    private static (MaxioBillingService Service, CatalogContext Context, StubMaxioHandler Handler) BuildService(
        StubMaxioHandler handler)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new CatalogContext(options);

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "stub-key", Password = "x" }
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);

        var service = new MaxioBillingService(
            client,
            new EfRepository<MaxioCustomerLink>(context),
            new EfRepository<MaxioSubscriptionClaim>(context),
            Options.Create(new MaxioOptions { ApiKey = "stub", Subdomain = "stub", ProductFamilyHandle = "stub-family" }),
            NullLogger<MaxioBillingService>.Instance);

        return (service, context, handler);
    }

    private static async Task SeedCustomerLinkAsync(IRepository<MaxioCustomerLink> repository)
    {
        await repository.AddAsync(new MaxioCustomerLink { UserId = UserId, MaxioCustomerId = 42 });
    }

    private static MaxioSubscriberIdentity Subscriber() => new(UserId, "shopper@example.com", "shopper@example.com");

    [TestMethod]
    public async Task ConnectionFailureWithNoSubscriptionMarksClaimUnknown()
    {
        var handler = new StubMaxioHandler(lookupFindsSubscription: false, failCreateWithConnectionError: true);
        var (service, context, _) = BuildService(handler);
        await SeedCustomerLinkAsync(new EfRepository<MaxioCustomerLink>(context));

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Subscriber(), PlanHandle, CancellationToken.None));

        Assert.AreEqual(MaxioBillingException.FailureKind.UnknownOutcome, ex.Kind);

        var claim = await new EfRepository<MaxioSubscriptionClaim>(context)
            .FirstOrDefaultAsync(new Microsoft.eShopWeb.ApplicationCore.Specifications.MaxioSubscriptionClaimByIdSpecification(Reference));
        Assert.IsNotNull(claim, "The claim row must exist before the write.");
        Assert.AreEqual(MaxioSubscriptionClaim.StatusUnknown, claim!.Status,
            "A connection failure on the write must be recorded as an unknown outcome, not a failure.");
        Assert.IsNull(claim.MaxioSubscriptionId);

        // The write was attempted exactly once (POSTs are never resent by the SDK). The
        // reference was re-read twice: once look-before-create, once to settle the outcome.
        Assert.AreEqual(1, handler.CreateSubscriptionAttempts);
        Assert.AreEqual(2, handler.LookupAttempts);
    }

    [TestMethod]
    public async Task ConnectionFailureWithLandedSubscriptionIsSettledByReference()
    {
        var handler = new StubMaxioHandler(lookupFindsSubscription: true, failCreateWithConnectionError: true);
        var (service, context, _) = BuildService(handler);
        await SeedCustomerLinkAsync(new EfRepository<MaxioCustomerLink>(context));

        var dto = await service.SubscribeAsync(Subscriber(), PlanHandle, CancellationToken.None);

        Assert.AreEqual(777, dto.MaxioSubscriptionId);
        Assert.IsTrue(dto.Created);
        Assert.AreEqual("active", dto.State);

        var claim = await new EfRepository<MaxioSubscriptionClaim>(context)
            .FirstOrDefaultAsync(new Microsoft.eShopWeb.ApplicationCore.Specifications.MaxioSubscriptionClaimByIdSpecification(Reference));
        Assert.IsNotNull(claim);
        Assert.AreEqual(MaxioSubscriptionClaim.StatusConfirmed, claim!.Status);
        Assert.AreEqual(777, claim.MaxioSubscriptionId);
    }

    /// <summary>
    /// Fakes the two Maxio endpoints the subscribe flow touches: the plan list, the
    /// subscription lookup by reference, and the CreateSubscription write (which fails
    /// at the transport level, so the write "may have landed").
    /// </summary>
    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private const string ProductsJson = """[{"product":{"id":1,"handle":"pro-plan","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"require_credit_card":false}}]""";
        private const string SubscriptionJson = """{"subscription":{"id":777,"reference":"eshopweb:u1:pro-plan","state":"active","currency":"USD"}}""";

        public int CreateSubscriptionAttempts { get; private set; }
        public int LookupAttempts { get; private set; }

        private readonly bool _lookupFindsSubscription;
        private readonly bool _failCreate;
        private bool _firstLookupDone;

        public StubMaxioHandler(bool lookupFindsSubscription, bool failCreateWithConnectionError)
        {
            _lookupFindsSubscription = lookupFindsSubscription;
            _failCreate = failCreateWithConnectionError;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
            {
                return Respond(HttpStatusCode.OK, ProductsJson);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json", StringComparison.Ordinal))
            {
                LookupAttempts++;
                // First lookup is the look-before-create read: it must miss so the write
                // is attempted. Later lookups are settlement re-reads.
                var found = _lookupFindsSubscription && _firstLookupDone;
                _firstLookupDone = true;
                return found
                    ? Respond(HttpStatusCode.OK, SubscriptionJson)
                    : Respond(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                CreateSubscriptionAttempts++;
                if (_failCreate)
                {
                    // The bytes may have reached the provider before the socket died —
                    // exactly the "outcome unknown" condition.
                    throw new HttpRequestException("connection reset");
                }
                return Respond(HttpStatusCode.OK, SubscriptionJson);
            }

            return Respond(HttpStatusCode.NotFound, string.Empty);
        }

        private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string json)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
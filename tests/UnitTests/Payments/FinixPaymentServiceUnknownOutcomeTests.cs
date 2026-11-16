using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

public class FinixPaymentServiceUnknownOutcomeTests
{
    [Fact]
    public async Task Pay_WhenAuthorizationConnectionFails_MarksClaimUnknownAndReturns503()
    {
        await using var db = CreateContext();
        var item = new CatalogItem(1, 1, "desc", "Widget", 12.34m, "widget.png");
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();

        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/identities"))
                return Json(HttpStatusCode.OK, """{"id":"ID_TEST","type":"PERSONAL","identity_roles":["BUYER"]}""");
            if (request.Method == HttpMethod.Post && path.EndsWith("/payment_instruments"))
                return Json(HttpStatusCode.OK, """{"id":"PI_TEST","last_four":"8210","brand":"MASTERCARD","expiration_month":12,"expiration_year":2029,"name":"Test Shopper"}""");
            if (path.Contains("/authorizations"))
                throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.NotFound, """{"total":1,"_embedded":{"errors":[{"code":"NOT_FOUND","message":"not found"}]}}""");
        });

        var service = CreateService(db, handler);
        var order = await service.CreateOrderAsync(
            "buyer@example.com",
            [new OrderLineInput(item.Id, 1)],
            null,
            CancellationToken.None);

        var card = new CardInput("5200828282828210", 12, 2029, "022", "Test Shopper", null);
        var exception = await Assert.ThrowsAsync<FinixPaymentException>(
            () => service.PayAsync("buyer@example.com", order.Id, null, card, CancellationToken.None));

        Assert.Equal(503, exception.StatusCode);
        Assert.Equal("FINIX_OUTCOME_UNKNOWN", exception.Code);

        var claim = await db.FinixOperationClaims.AsNoTracking()
            .SingleAsync(c => c.ClaimKey == $"pay:{order.Id}");
        Assert.Equal("Unknown", claim.Status);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }

    private static FinixPaymentService CreateService(CatalogContext db, HttpMessageHandler handler)
    {
        var client = new FinixApiReferenceClient(
            new HttpClient(handler),
            new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "test", Password = "test" },
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Disabled()
            });

        var options = new FinixOptions
        {
            Username = "test",
            Password = "test",
            MerchantId = "MERCHANT_TEST",
            Environment = "sandbox",
            Currency = "USD"
        };
        options.Validate();

        return new FinixPaymentService(db, client, options, NullLogger<FinixPaymentService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }
}

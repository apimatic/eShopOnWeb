using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Finix;

[TestClass]
public class FinixPaymentServiceTests
{
    private const string CardJson =
        """{"id":"PI_TEST","identity":"ID_TEST","enabled":true,"last_four":"8210","brand":"MASTERCARD","expiration_month":12,"expiration_year":2029,"name":"Test Shopper","type":"PAYMENT_CARD"}""";

    private const string AuthorizationJson =
        """{"id":"AU_TEST","state":"SUCCEEDED","amount":1950,"currency":"USD","merchant":"MERCHANT","source":"PI_TEST","is_void":false,"transfer":null,"idempotency_id":"idem"}""";

    private const string CapturedAuthorizationJson =
        """{"id":"AU_TEST","state":"SUCCEEDED","amount":1950,"currency":"USD","merchant":"MERCHANT","source":"PI_TEST","is_void":false,"transfer":"TR_CAP"}""";

    private const string VoidedAuthorizationJson =
        """{"id":"AU_TEST","state":"SUCCEEDED","amount":1950,"currency":"USD","merchant":"MERCHANT","source":"PI_TEST","is_void":true,"void_state":"SUCCEEDED","transfer":null}""";

    private const string TransferJson =
        """{"id":"TR_CAP","state":"SUCCEEDED","type":"DEBIT","amount":1950,"fee":50,"currency":"USD","merchant":"MERCHANT"}""";

    private const string RefundJson =
        """{"id":"TR_REF","state":"SUCCEEDED","type":"REVERSAL","amount":500,"currency":"USD","parent_transfer":"TR_CAP"}""";

    private const string EmptyReversalsJson =
        """{"_embedded":{"transfers":[]},"page":{"next_cursor":null}}""";

    [TestMethod]
    public async Task Pay_AuthorizesOnce_ThenFulfilCapturesWithFeeAndNet()
    {
        var (service, handler, db) = CreateService();
        using var _ = db;
        var item = await SeedItemAsync(db);

        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        Assert.AreEqual(1950, order.TotalCents);

        var pay = await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);
        Assert.AreEqual("Authorized", pay.PaymentStatus);
        Assert.AreEqual("AU_TEST", pay.AuthorizationId);
        Assert.AreEqual(1950, pay.AmountCents);

        var authorizationsBefore = Count(handler, HttpMethod.Post, "/authorizations");
        var repeat = await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);
        Assert.AreEqual(pay.AuthorizationId, repeat.AuthorizationId);
        Assert.AreEqual(authorizationsBefore, Count(handler, HttpMethod.Post, "/authorizations"));

        var fulfil = await service.FulfilOrderAsync(order.OrderId, CancellationToken.None);
        Assert.AreEqual("Captured", fulfil.PaymentStatus);
        Assert.AreEqual("TR_CAP", fulfil.CaptureTransferId);
        Assert.AreEqual(1950, fulfil.CapturedAmountCents);
        Assert.AreEqual(50, fulfil.FeeCents);
        Assert.AreEqual(1900, fulfil.NetCents);

        var capturesBefore = Count(handler, HttpMethod.Put, "/authorizations/AU_TEST");
        var repeatFulfil = await service.FulfilOrderAsync(order.OrderId, CancellationToken.None);
        Assert.AreEqual(fulfil.CaptureTransferId, repeatFulfil.CaptureTransferId);
        Assert.AreEqual(capturesBefore, Count(handler, HttpMethod.Put, "/authorizations/AU_TEST"));
    }

    [TestMethod]
    public async Task Refund_IsIdempotentPerKey_AndDistinctPartialsStayWithinCapture()
    {
        var (service, handler, db) = CreateService();
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);
        await service.FulfilOrderAsync(order.OrderId, CancellationToken.None);

        var first = await service.RefundOrderAsync("shopper-a", order.OrderId,
            new RefundOrderRequest { Amount = 5.00m, IdempotencyKey = "k1" }, CancellationToken.None);
        Assert.AreEqual("TR_REF", first.RefundId);
        Assert.AreEqual(500, first.RefundedCents);

        var reversalsAfterFirst = Count(handler, HttpMethod.Post, "/transfers/TR_CAP/reversals");
        var repeated = await service.RefundOrderAsync("shopper-a", order.OrderId,
            new RefundOrderRequest { Amount = 5.00m, IdempotencyKey = "k1" }, CancellationToken.None);
        Assert.AreEqual(first.RefundId, repeated.RefundId);
        Assert.AreEqual(reversalsAfterFirst, Count(handler, HttpMethod.Post, "/transfers/TR_CAP/reversals"));

        var second = await service.RefundOrderAsync("shopper-a", order.OrderId,
            new RefundOrderRequest { Amount = 5.00m, IdempotencyKey = "k2" }, CancellationToken.None);
        Assert.AreEqual(1000, second.RefundedCents);

        await Assert.ThrowsExceptionAsync<PaymentValidationException>(() =>
            service.RefundOrderAsync("shopper-a", order.OrderId,
                new RefundOrderRequest { Amount = 10.00m, IdempotencyKey = "k3" }, CancellationToken.None));
    }

    [TestMethod]
    public async Task Cancel_VoidsTheHold_AndCaptureIsRefused()
    {
        var (service, _, db) = CreateService();
        using var _1 = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);

        var cancel = await service.CancelOrderAsync(order.OrderId, CancellationToken.None);
        Assert.AreEqual("Voided", cancel.PaymentStatus);
        Assert.IsNotNull(cancel.VoidedAt);

        await Assert.ThrowsExceptionAsync<PaymentConflictException>(() =>
            service.FulfilOrderAsync(order.OrderId, CancellationToken.None));
    }

    [TestMethod]
    public async Task SavedCard_IsScopedToShopper_AndDeleteMakesItUnusable()
    {
        var (service, _, db) = CreateService();
        using var _ = db;
        var item = await SeedItemAsync(db);

        var saved = await service.SaveCardAsync("shopper-a", NewCardSave(), CancellationToken.None);
        Assert.AreEqual("PI_TEST", saved.PaymentMethodId);
        Assert.AreEqual("8210", saved.Last4);
        Assert.AreEqual("MASTERCARD", saved.Brand);

        var mine = await service.GetCardsAsync("shopper-a", CancellationToken.None);
        Assert.AreEqual(1, mine.PaymentMethods.Count);
        var theirs = await service.GetCardsAsync("shopper-b", CancellationToken.None);
        Assert.AreEqual(0, theirs.PaymentMethods.Count);

        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        var pay = await service.PayOrderAsync("shopper-a", order.OrderId,
            new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId }, CancellationToken.None);
        Assert.AreEqual("Authorized", pay.PaymentStatus);

        await service.DeleteCardAsync("shopper-a", saved.PaymentMethodId, CancellationToken.None);
        var afterDelete = await service.GetCardsAsync("shopper-a", CancellationToken.None);
        Assert.AreEqual(0, afterDelete.PaymentMethods.Count);

        var nextOrder = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await Assert.ThrowsExceptionAsync<PaymentNotFoundException>(() =>
            service.PayOrderAsync("shopper-a", nextOrder.OrderId,
                new PayOrderRequest { PaymentMethodId = saved.PaymentMethodId }, CancellationToken.None));
    }

    [TestMethod]
    public async Task Reconciliation_WalksEveryPageOfBothFinixLists()
    {
        var (service, handler, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri!.Query;

            if (path == "/transfers" && query.Contains("after_cursor=c2", StringComparison.Ordinal))
            {
                return Json("""{"_embedded":{"transfers":[{"id":"TR2","state":"SUCCEEDED","type":"DEBIT","amount":200,"currency":"USD"}]},"page":{"next_cursor":null}}""");
            }

            if (path == "/transfers")
            {
                return Json("""{"_embedded":{"transfers":[{"id":"TR1","state":"SUCCEEDED","type":"DEBIT","amount":100,"currency":"USD"}]},"page":{"next_cursor":"c2"}}""");
            }

            if (path == "/authorizations")
            {
                return Json("""{"_embedded":{"authorizations":[{"id":"AU1","state":"SUCCEEDED","amount":100,"currency":"USD"}]},"page":{"next_cursor":null}}""");
            }

            return Json("{}");
        });
        using var _ = db;

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var report = await service.ReconcileAsync(from, to, CancellationToken.None);

        Assert.IsFalse(report.Truncated);
        CollectionAssert.AreEquivalent(new[] { "TR1", "TR2", "AU1" },
            report.FinixTransactions.Select(t => t.Id).ToArray());
        Assert.AreEqual(2, Count(handler, HttpMethod.Get, "/transfers"));
        Assert.AreEqual(1, Count(handler, HttpMethod.Get, "/authorizations"));
    }

    [TestMethod]
    public async Task Pay_OnTransportFailure_SettlesFromTheProviderIdempotencyRecord()
    {
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/authorizations")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/authorizations")
            {
                return Json("""{"_embedded":{"authorizations":[{"id":"AU_TEST","state":"SUCCEEDED","amount":1950,"currency":"USD","merchant":"MERCHANT","source":"PI_TEST","is_void":false,"transfer":null}]},"page":{"next_cursor":null}}""");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);

        var pay = await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);

        Assert.AreEqual("Authorized", pay.PaymentStatus);
        Assert.AreEqual("AU_TEST", pay.AuthorizationId);
        Assert.AreEqual(PaymentStatus.Authorized, db.OrderPayments.Single().Status);
    }

    [TestMethod]
    public async Task Pay_OnTransportFailure_WithNoProviderRecord_LeavesOutcomeUnknown()
    {
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/authorizations")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/authorizations")
            {
                return Json("""{"_embedded":{"authorizations":[]},"page":{"next_cursor":null}}""");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<PaymentProviderException>(() =>
            service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None));

        Assert.IsTrue(error.OutcomeUnknown);
        Assert.AreEqual(PaymentStatus.Unknown, db.OrderPayments.Single().Status);
    }

    [TestMethod]
    public async Task Fulfil_OnTransportFailure_ReconcilesTheCapture()
    {
        var captureAttempted = false;
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == "/authorizations/AU_TEST" &&
                body?.Contains("capture_amount", StringComparison.Ordinal) == true)
            {
                captureAttempted = true;
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/authorizations/AU_TEST")
            {
                return Json(captureAttempted ? CapturedAuthorizationJson : AuthorizationJson);
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);

        var fulfil = await service.FulfilOrderAsync(order.OrderId, CancellationToken.None);

        Assert.AreEqual("Captured", fulfil.PaymentStatus);
        Assert.AreEqual("TR_CAP", fulfil.CaptureTransferId);
        Assert.AreEqual(50, fulfil.FeeCents);
    }

    [TestMethod]
    public async Task Cancel_OnTransportFailure_ReconcilesTheVoid()
    {
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path == "/authorizations/AU_TEST" &&
                body?.Contains("void_me", StringComparison.Ordinal) == true)
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/authorizations/AU_TEST")
            {
                return Json(VoidedAuthorizationJson);
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);

        var cancel = await service.CancelOrderAsync(order.OrderId, CancellationToken.None);

        Assert.AreEqual("Voided", cancel.PaymentStatus);
        Assert.IsNotNull(cancel.VoidedAt);
    }

    [TestMethod]
    public async Task Refund_OnTransportFailure_AdoptsTheNewReversal()
    {
        var reversalCalls = 0;
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/transfers/TR_CAP/reversals")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/transfers/TR_CAP/reversals")
            {
                reversalCalls++;
                return reversalCalls == 1
                    ? Json(EmptyReversalsJson)
                    : Json("""{"_embedded":{"transfers":[{"id":"TR_REF","state":"SUCCEEDED","type":"REVERSAL","amount":500,"currency":"USD","parent_transfer":"TR_CAP"}]},"page":{"next_cursor":null}}""");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;
        var item = await SeedItemAsync(db);
        var order = await service.CreateOrderAsync("shopper-a", NewOrder(item.Id), CancellationToken.None);
        await service.PayOrderAsync("shopper-a", order.OrderId, NewCardPayment(), CancellationToken.None);
        await service.FulfilOrderAsync(order.OrderId, CancellationToken.None);

        var refund = await service.RefundOrderAsync("shopper-a", order.OrderId,
            new RefundOrderRequest { Amount = 5.00m, IdempotencyKey = "k1" }, CancellationToken.None);

        Assert.AreEqual("TR_REF", refund.RefundId);
        Assert.AreEqual(500, refund.RefundedCents);
    }
    [TestMethod]
    public async Task SaveCard_OnIdentityTransportFailure_AdoptsTheTaggedIdentity()
    {
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/identities")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/identities")
            {
                return Json("""{"_embedded":{"identities":[{"id":"ID_TEST"}]},"page":{"next_cursor":null}}""");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;

        var saved = await service.SaveCardAsync("shopper-a", NewCardSave(), CancellationToken.None);

        Assert.AreEqual("PI_TEST", saved.PaymentMethodId);
        Assert.AreEqual("ID_TEST", db.ShopperFinixIdentities.Single().IdentityId);
    }

    [TestMethod]
    public async Task SaveCard_OnInstrumentTransportFailure_AdoptsTheTaggedInstrument()
    {
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/payment_instruments")
            {
                throw new HttpRequestException("connection reset");
            }

            if (request.Method == HttpMethod.Get && path == "/payment_instruments")
            {
                return Json("{\"_embedded\":{\"payment_instruments\":[" + CardJson + "],\"page\":{\"next_cursor\":null}}}");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;

        var saved = await service.SaveCardAsync("shopper-a", NewCardSave(), CancellationToken.None);

        Assert.AreEqual("PI_TEST", saved.PaymentMethodId);
        Assert.AreEqual(1, db.SavedCards.Count());
    }

    [TestMethod]
    public async Task Delete_OnTransportFailure_ReconcilesTheDisabledCard()
    {
        var disableFailed = false;
        var (service, _, db) = CreateService((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (disableFailed && request.Method == HttpMethod.Put && path == "/payment_instruments/PI_TEST")
            {
                throw new HttpRequestException("connection reset");
            }

            if (disableFailed && request.Method == HttpMethod.Get && path == "/payment_instruments/PI_TEST")
            {
                return Json("""{"id":"PI_TEST","identity":"ID_TEST","enabled":false,"last_four":"8210","brand":"MASTERCARD"}""");
            }

            return DefaultResponder(request, body);
        });
        using var _ = db;

        var saved = await service.SaveCardAsync("shopper-a", NewCardSave(), CancellationToken.None);
        disableFailed = true;

        var deleted = await service.DeleteCardAsync("shopper-a", saved.PaymentMethodId, CancellationToken.None);

        Assert.IsTrue(deleted.Deleted);
        Assert.AreEqual(0, db.SavedCards.Count());
    }
    private static (FinixPaymentService Service, StubHandler Handler, CatalogContext Db) CreateService(
        Func<HttpRequestMessage, string?, HttpResponseMessage>? responder = null)
    {
        var handler = new StubHandler(responder ?? DefaultResponder);
        var client = new FinixApiReferenceClient(new HttpClient(handler), new FinixApiReferenceClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-user", Password = "test-password" },
            Retry = RetryOptions.Default() with { MaxRetries = 0 },
            Server = { Default = { Production = { BaseUrl = "https://stub.local" } } }
        });

        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"finix-tests-{Guid.NewGuid():N}")
            .Options;
        var db = new CatalogContext(options);

        var service = new FinixPaymentService(
            client,
            db,
            new FinixOptions
            {
                Username = "test-user",
                Password = "test-password",
                MerchantId = "MERCHANT",
                Environment = "sandbox",
                Currency = "USD"
            },
            NullLogger<FinixPaymentService>.Instance,
            TimeProvider.System);

        return (service, handler, db);
    }

    private static async Task<CatalogItem> SeedItemAsync(CatalogContext db)
    {
        var item = new CatalogItem(1, 1, "A test item", "Test Item", 19.50m, "pic.png");
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static CreateOrderRequest NewOrder(int catalogItemId) => new()
    {
        Items = [new CreateOrderItemRequest { CatalogItemId = catalogItemId, Quantity = 1 }],
        ShipToAddress = new ShippingAddressRequest
        {
            Street = "1 Main St",
            City = "Seattle",
            State = "WA",
            Country = "USA",
            ZipCode = "98101"
        }
    };

    private static PayOrderRequest NewCardPayment() => new()
    {
        Card = new CardDetailsRequest
        {
            Number = "5200828282828210",
            ExpirationMonth = 12,
            ExpirationYear = 2029,
            SecurityCode = "022",
            Name = "Test Shopper"
        }
    };

    private static SavePaymentMethodRequest NewCardSave() => new()
    {
        Number = "5200828282828210",
        ExpirationMonth = 12,
        ExpirationYear = 2029,
        SecurityCode = "022",
        Name = "Test Shopper"
    };

    private static int Count(StubHandler handler, HttpMethod method, string path) =>
        handler.Requests.Count(r => r.Method == method && r.Path == path);

    private static HttpResponseMessage DefaultResponder(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Post && path == "/identities")
        {
            return Json("""{"id":"ID_TEST"}""");
        }

        if (request.Method == HttpMethod.Post && path == "/payment_instruments")
        {
            return Json(CardJson);
        }

        if (request.Method == HttpMethod.Get && path == "/payment_instruments/PI_TEST")
        {
            return Json(CardJson);
        }

        if (request.Method == HttpMethod.Put && path == "/payment_instruments/PI_TEST")
        {
            return Json("""{"id":"PI_TEST","enabled":false}""");
        }

        if (request.Method == HttpMethod.Post && path == "/authorizations")
        {
            return Json(AuthorizationJson);
        }

        if (request.Method == HttpMethod.Get && path == "/authorizations/AU_TEST")
        {
            return Json(AuthorizationJson);
        }

        if (request.Method == HttpMethod.Put && path == "/authorizations/AU_TEST")
        {
            return body?.Contains("capture_amount", StringComparison.Ordinal) == true
                ? Json(CapturedAuthorizationJson)
                : Json(VoidedAuthorizationJson);
        }

        if (request.Method == HttpMethod.Get && path == "/transfers/TR_CAP")
        {
            return Json(TransferJson);
        }

        if (request.Method == HttpMethod.Get && path == "/transfers/TR_CAP/reversals")
        {
            return Json(EmptyReversalsJson);
        }

        if (request.Method == HttpMethod.Post && path == "/transfers/TR_CAP/reversals")
        {
            return Json(RefundJson);
        }

        return Json("{}");
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) =>
            _responder = responder;

        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));

            var response = _responder(request, body);
            response.RequestMessage = request;
            return response;
        }
    }
}

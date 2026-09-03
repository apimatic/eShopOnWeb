using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class PaymentApplicationServiceTests
{
    [Fact]
    public async Task RepeatedPayDoesNotAuthorizeTwice()
    {
        await using var db = CreateContext();
        var gateway = Gateway();
        var service = Service(db, gateway);
        var orderId = (await service.CreateOrderAsync("buyer-a", OrderRequest(db), default)).OrderId;

        var first = await service.PayAsync("buyer-a", orderId, new PayOrderRequest { Card = Card() }, default);
        var repeated = await service.PayAsync("buyer-a", orderId, new PayOrderRequest { Card = Card() }, default);

        Assert.Equal(first.AuthorizationId, repeated.AuthorizationId);
        await gateway.Received(1).AuthorizeAsync(orderId, Arg.Any<string>(), 20m, "USD", Arg.Any<CardSource>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavedCardCannotBeUsedByAnotherShopper()
    {
        await using var db = CreateContext();
        var gateway = Gateway();
        var service = Service(db, gateway);
        var method = await service.SavePaymentMethodAsync("buyer-a",
            new SavePaymentMethodRequest { Card = Card() }, default);
        var orderId = (await service.CreateOrderAsync("buyer-b", OrderRequest(db), default)).OrderId;

        var error = await Assert.ThrowsAsync<PaymentDomainException>(() => service.PayAsync("buyer-b", orderId,
            new PayOrderRequest { PaymentMethodId = method.PaymentMethodId }, default));

        Assert.Equal(404, error.StatusCode);
        await gateway.DidNotReceive().AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameRefundKeyIsIdempotentAndDistinctPartialRefundsAreAllowed()
    {
        await using var db = CreateContext();
        var gateway = Gateway();
        var service = Service(db, gateway);
        var orderId = (await service.CreateOrderAsync("buyer-a", OrderRequest(db), default)).OrderId;
        await service.PayAsync("buyer-a", orderId, new PayOrderRequest { Card = Card() }, default);
        await service.FulfilAsync(orderId, default);

        var first = await service.RefundAsync("buyer-a", orderId,
            new RefundOrderRequest { IdempotencyKey = "return-one", Amount = 5m }, default);
        var repeated = await service.RefundAsync("buyer-a", orderId,
            new RefundOrderRequest { IdempotencyKey = "return-one", Amount = 5m }, default);
        var second = await service.RefundAsync("buyer-a", orderId,
            new RefundOrderRequest { IdempotencyKey = "return-two", Amount = 3m }, default);

        Assert.Equal(first.RefundId, repeated.RefundId);
        Assert.NotEqual(first.RefundId, second.RefundId);
        Assert.Equal(2, await db.PaymentRefunds.CountAsync());
        await gateway.Received(2).RefundAsync(orderId, Arg.Any<string>(), "capture-id", Arg.Any<decimal>(),
            "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilRenewsAnAuthorizationThatPayPalReportsAsStale()
    {
        await using var db = CreateContext();
        var gateway = Gateway();
        gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("authorization-id", "CREATED", 20m, "USD",
                DateTimeOffset.UtcNow.AddMinutes(-1)));
        gateway.ReauthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("renewed-authorization", "CREATED", 20m, "USD",
                DateTimeOffset.UtcNow.AddDays(3)));
        var service = Service(db, gateway);
        var orderId = (await service.CreateOrderAsync("buyer-a", OrderRequest(db), default)).OrderId;
        await service.PayAsync("buyer-a", orderId, new PayOrderRequest { Card = Card() }, default);

        var result = await service.FulfilAsync(orderId, default);

        Assert.Equal("Captured", result.PaymentStatus);
        await gateway.Received(1).ReauthorizeAsync(orderId, Arg.Any<string>(), "authorization-id", 20m,
            "USD", Arg.Any<CancellationToken>());
        await gateway.Received(1).CaptureAsync(orderId, Arg.Any<string>(), "renewed-authorization", 20m,
            "USD", Arg.Any<CancellationToken>());
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CatalogContext(options);
        db.CatalogItems.Add(new CatalogItem(1, 1, "Description", "Test item", 10m, "test.png"));
        db.SaveChanges();
        return db;
    }

    private static PaymentApplicationService Service(CatalogContext db, IPayPalGateway gateway) =>
        new(db, gateway, Options.Create(new PayPalSettings
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Environment = "Sandbox",
            Currency = "USD"
        }), NullLogger<PaymentApplicationService>.Instance);

    private static IPayPalGateway Gateway()
    {
        var gateway = Substitute.For<IPayPalGateway>();
        gateway.AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(info => new AuthorizationResult("provider-order", "authorization-id", "CREATED",
                info.ArgAt<decimal>(2), DateTimeOffset.UtcNow.AddDays(3)));
        gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("authorization-id", "CREATED", 20m, "USD",
                DateTimeOffset.UtcNow.AddDays(3)));
        gateway.ReauthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(info => new ReauthorizationResult("renewed-authorization", "CREATED",
                info.ArgAt<decimal>(3), info.ArgAt<string>(4), DateTimeOffset.UtcNow.AddDays(3)));
        gateway.CaptureAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(info => new CaptureResult("capture-id", "COMPLETED", info.ArgAt<decimal>(3), 1m, 19m));
        gateway.RefundAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(info => new ProviderRefundResult($"refund-{info.ArgAt<string>(5)}", "COMPLETED",
                info.ArgAt<decimal>(3)));
        gateway.GetRefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(info => new ProviderRefundResult(info.ArgAt<string>(0), "COMPLETED",
                info.ArgAt<decimal>(1)));
        gateway.SaveCardAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CardRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new VaultResult("vault-token", "customer", "VISA", "1111", "2099-12"));
        return gateway;
    }

    private static CreateOrderRequest OrderRequest(CatalogContext db) => new()
    {
        Items = [new OrderLineRequest { CatalogItemId = db.CatalogItems.Single().Id, Quantity = 2 }],
        ShippingAddress = Address()
    };

    private static CardRequest Card() => new()
    {
        Number = "4111111111111111",
        Expiry = "2099-12",
        SecurityCode = "123",
        Name = "Test Shopper",
        BillingAddress = Address()
    };

    private static PostalAddressRequest Address() => new()
    {
        Street = "1 Main Street",
        City = "San Jose",
        State = "CA",
        Country = "US",
        ZipCode = "95131"
    };
}

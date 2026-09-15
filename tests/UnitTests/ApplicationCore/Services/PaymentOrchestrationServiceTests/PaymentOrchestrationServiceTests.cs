using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentOrchestrationServiceTests;

public class PaymentOrchestrationServiceTests
{
    private const string Buyer = "demouser@microsoft.com";
    private const string Currency = "USD";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<OrderPayment> _payments = Substitute.For<IRepository<OrderPayment>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentOrchestrationService> _logger = Substitute.For<IAppLogger<PaymentOrchestrationService>>();

    private PaymentOrchestrationService CreateSut()
    {
        var settings = Options.Create(new global::Microsoft.eShopWeb.ApplicationCore.PayPalSettings { Currency = Currency, Environment = "sandbox" });
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        return new PaymentOrchestrationService(_orders, _items, _payments, _methods, _payPal, _uriComposer, settings, _logger);
    }

    private static Order OrderFor(string buyer)
    {
        var address = new Address("1 Main", "Redmond", "WA", "US", "98052");
        var itemOrdered = new CatalogItemOrdered(1, "Sweatshirt", "pic.png");
        return new Order(buyer, address, new List<OrderItem> { new OrderItem(itemOrdered, 19.50m, 2) });
    }

    private static OrderPayment AuthorizedPayment(decimal amount = 47.50m)
    {
        var payment = new OrderPayment(1, Buyer, amount, Currency);
        payment.SetPayPalOrderId("PPO-1");
        payment.MarkAuthorized("AUTH-1", "CREATED", DateTimeOffset.Now.AddDays(3));
        return payment;
    }

    private static OrderPayment CapturedPayment(decimal captured = 47.50m)
    {
        var payment = AuthorizedPayment(captured);
        payment.MarkCaptured("CAP-1", "COMPLETED", captured, 1.72m, captured - 1.72m);
        return payment;
    }

    // ---------------- Place order ----------------

    [Fact]
    public async Task PlaceOrder_CreatesOrderAndPayment_WithTotalToTheCent()
    {
        var catalog = new CatalogItem(2, 2, "desc", "Sweatshirt", 19.50m, "pic.png");
        typeof(BaseEntity).GetProperty("Id")!.SetValue(catalog, 1);
        _items.ListAsync(Arg.Any<ISpecification<CatalogItem>>(), Arg.Any<CancellationToken>()).Returns(new List<CatalogItem> { catalog });
        _orders.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());

        var sut = CreateSut();
        var result = await sut.PlaceOrderAsync(Buyer, new[] { new OrderLineCommand(1, 2) }, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(39.00m, result.Value!.Total);
        Assert.Equal("AwaitingPayment", result.Value.Status);
        await _payments.Received(1).AddAsync(Arg.Any<OrderPayment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrder_UnknownCatalogItem_IsInvalid()
    {
        _items.ListAsync(Arg.Any<ISpecification<CatalogItem>>(), Arg.Any<CancellationToken>()).Returns(new List<CatalogItem>());
        var sut = CreateSut();

        var result = await sut.PlaceOrderAsync(Buyer, new[] { new OrderLineCommand(999, 1) }, null, CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Invalid, result.Status);
        await _orders.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    // ---------------- Authorize (pay) ----------------

    [Fact]
    public async Task Authorize_OtherBuyersOrder_NotFound()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(OrderFor("someone-else"));
        var sut = CreateSut();

        var result = await sut.AuthorizeAsync(Buyer, 1, new PayCommand(null, new CardCommand(null, "4111111111111111", "2027-12", "123", null)), CancellationToken.None);

        Assert.Equal(PaymentResultStatus.NotFound, result.Status);
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<PayPalCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent_AndDoesNotCallPayPal()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(AuthorizedPayment());
        var sut = CreateSut();

        var result = await sut.AuthorizeAsync(Buyer, 1, new PayCommand(null, new CardCommand(null, "4111111111111111", "2027-12", "123", null)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Authorized", result.Value!.PaymentStatus);
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<PayPalCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_MissingCardAndSavedCard_IsInvalid()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(new OrderPayment(1, Buyer, 47.50m, Currency));
        var sut = CreateSut();

        var result = await sut.AuthorizeAsync(Buyer, 1, new PayCommand(null, null), CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Authorize_WhenPayPalRequiresApproval_ReportsAndDoesNotAuthorize()
    {
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        var payment = new OrderPayment(1, Buyer, 47.50m, Currency);
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        _payPal.AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<PayPalCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { PayPalOrderId = "PPO-1", RequiresApproval = true, ApprovalDetail = "needs browser" });
        var sut = CreateSut();

        var result = await sut.AuthorizeAsync(Buyer, 1, new PayCommand(null, new CardCommand(null, "4111111111111111", "2027-12", "123", null)), CancellationToken.None);

        Assert.Equal(PaymentResultStatus.RequiresApproval, result.Status);
        Assert.Equal(PaymentStatus.RequiresApproval, payment.Status);
    }

    // ---------------- Fulfil (capture + reauth on stale) ----------------

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var payment = AuthorizedPayment();
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        _payPal.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult { CaptureId = "CAP-1", Status = "COMPLETED", CapturedAmount = 47.50m, PayPalFee = 1.72m, NetAmount = 45.78m });
        var sut = CreateSut();

        var result = await sut.FulfilAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(1.72m, payment.PayPalFee);
        Assert.Equal(45.78m, payment.NetAmount);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_RenewsThenCaptures()
    {
        var payment = AuthorizedPayment();
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);

        var captureCalls = 0;
        _payPal.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captureCalls++;
                if (captureCalls == 1)
                {
                    throw new PayPalException("authorization expired", isBusinessRule: true);
                }
                return new PayPalCaptureResult { CaptureId = "CAP-2", Status = "COMPLETED", CapturedAmount = 47.50m, PayPalFee = 1.72m, NetAmount = 45.78m };
            });
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult { AuthorizationId = "AUTH-2", AuthorizationStatus = "CREATED", ExpiresAt = DateTimeOffset.Now.AddDays(3) });
        var sut = CreateSut();

        var result = await sut.FulfilAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("AUTH-2", payment.AuthorizationId);
        await _payPal.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationCannotBeRenewed_ReturnsActionableConflict()
    {
        var payment = AuthorizedPayment();
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        _payPal.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalException("expired", isBusinessRule: true));
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalException("cannot reauthorize", isBusinessRule: true));
        var sut = CreateSut();

        var result = await sut.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Conflict, result.Status);
        Assert.Contains("place and pay for a new order", result.Error);
    }

    [Fact]
    public async Task Fulfil_WhenNotAuthorized_IsConflict()
    {
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(new OrderPayment(1, Buyer, 47.50m, Currency));
        var sut = CreateSut();

        var result = await sut.FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Conflict, result.Status);
    }

    // ---------------- Cancel (void) ----------------

    [Fact]
    public async Task Cancel_VoidsAuthorization()
    {
        var payment = AuthorizedPayment();
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        var sut = CreateSut();

        var result = await sut.CancelAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        await _payPal.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AfterCapture_IsConflict()
    {
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(CapturedPayment());
        var sut = CreateSut();

        var result = await sut.CancelAsync(1, CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Conflict, result.Status);
        await _payPal.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ---------------- Refund ----------------

    [Fact]
    public async Task Refund_ExceedingRemaining_IsRejectedBeforeCallingPayPal()
    {
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(CapturedPayment(47.50m));
        var sut = CreateSut();

        var result = await sut.RefundAsync(Buyer, isAdmin: false, 1, 100m, "key-1", CancellationToken.None);

        Assert.Equal(PaymentResultStatus.Invalid, result.Status);
        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedKey_ReturnsExistingWithoutRefundingTwice()
    {
        var payment = CapturedPayment(47.50m);
        payment.AddRefund(new OrderRefund("key-1", 10m, Currency, "PP-REF-1", "COMPLETED"));
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        var sut = CreateSut();

        var result = await sut.RefundAsync(Buyer, isAdmin: false, 1, 10m, "key-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10m, result.Value!.TotalRefunded);
        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_OtherBuyersOrder_NotFound()
    {
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor("someone-else"));
        var sut = CreateSut();

        var result = await sut.RefundAsync(Buyer, isAdmin: false, 1, 10m, "key-1", CancellationToken.None);

        Assert.Equal(PaymentResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Refund_PartialSucceeds_AndAdvancesToPartiallyRefunded()
    {
        var payment = CapturedPayment(47.50m);
        _orders.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(OrderFor(Buyer));
        _payments.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>()).Returns(payment);
        _payPal.RefundAsync("CAP-1", 10m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult { RefundId = "PP-REF-1", Status = "COMPLETED", Amount = 10m, TotalRefunded = 10m });
        var sut = CreateSut();

        var result = await sut.RefundAsync(Buyer, isAdmin: false, 1, 10m, "key-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PartiallyRefunded", result.Value!.PaymentStatus);
        Assert.Equal(10m, payment.TotalRefunded());
    }

    // ---------------- Saved cards ----------------

    [Fact]
    public async Task SaveCard_ReturnsSafeDescriptor_AndPersists()
    {
        _payPal.VaultCardAsync(Arg.Any<PayPalCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalVaultResult { VaultId = "VAULT-1", CardBrand = "VISA", LastFourDigits = "1111", Expiry = "2027-12", CardholderName = "Demo User" });
        _methods.AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<PaymentMethod>());
        var sut = CreateSut();

        var result = await sut.SaveCardAsync(Buyer, new CardCommand("Demo User", "4111111111111111", "2027-12", "123", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("VISA", result.Value!.CardBrand);
        Assert.Equal("1111", result.Value.LastFourDigits);
        await _methods.Received(1).AddAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_OtherBuyer_NotFound_AndDoesNotCallPayPal()
    {
        var method = new PaymentMethod("someone-else", "VAULT-1", "VISA", "1111", "2027-12", "Other");
        _methods.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(method);
        var sut = CreateSut();

        var result = await sut.DeleteSavedCardAsync(Buyer, 5, CancellationToken.None);

        Assert.Equal(PaymentResultStatus.NotFound, result.Status);
        await _payPal.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCard_OwnCard_RemovesLocallyAndInVault()
    {
        var method = new PaymentMethod(Buyer, "VAULT-1", "VISA", "1111", "2027-12", "Demo User");
        _methods.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(method);
        var sut = CreateSut();

        var result = await sut.DeleteSavedCardAsync(Buyer, 5, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _payPal.Received(1).DeleteVaultedCardAsync("VAULT-1", Arg.Any<CancellationToken>());
        await _methods.Received(1).DeleteAsync(method, Arg.Any<CancellationToken>());
    }
}

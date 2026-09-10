using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private const string Buyer = "buyer@test";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<OrderPayment> _paymentRepo = Substitute.For<IRepository<OrderPayment>>();
    private readonly IReadRepository<CatalogItem> _catalogRepo = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly ISavedCardService _savedCards = Substitute.For<ISavedCardService>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _paymentRepo, _catalogRepo, _savedCards, _gateway, _uriComposer);

    private void RepoReturns(OrderPayment payment) =>
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderPayment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    private static OrderPayment Authorized(string buyer = Buyer, string? expiresAt = null)
    {
        var payment = new OrderPayment(1, buyer, "USD", 50m);
        payment.SetInvoiceReference("eshop-1-abc");
        payment.MarkAuthorized("PPORDER", "AUTH1", "CREATED",
            expiresAt ?? DateTimeOffset.UtcNow.AddDays(28).ToString("O"));
        return payment;
    }

    private static OrderPayment Captured()
    {
        var payment = Authorized();
        payment.MarkFulfilled("CAP1", "COMPLETED", 50m, 2m, 48m);
        return payment;
    }

    private static CardDetails Card() => new() { Number = "4111111111111111", Expiry = "2027-01", SecurityCode = "123" };

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_IsIdempotentAndDoesNotCallGateway()
    {
        RepoReturns(Authorized());
        var service = CreateService();

        var view = await service.PayAsync(Buyer, 1, Card(), null, CancellationToken.None);

        Assert.Equal("Authorized", view.Status);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<CardDetails?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_ForOrderOwnedByAnotherShopper_IsNotFound()
    {
        RepoReturns(Authorized(buyer: "someone-else@test"));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentEntityNotFoundException>(
            () => service.PayAsync(Buyer, 1, Card(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Pay_WithNeitherCardNorSavedMethod_IsValidationError()
    {
        RepoReturns(new OrderPayment(1, Buyer, "USD", 50m));
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentValidationException>(
            () => service.PayAsync(Buyer, 1, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Refund_RepeatedUnderSameKey_ReturnsPriorRefundWithoutCallingGateway()
    {
        var payment = Captured();
        payment.AddRefund(new PaymentRefund("dup", 10m, "R1", "COMPLETED"));
        RepoReturns(payment);
        var service = CreateService();

        var (_, refund) = await service.RefundAsync(Buyer, 1, 10m, "dup", CancellationToken.None);

        Assert.Equal("R1", refund.RefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_BeyondRemaining_IsRejectedAndDoesNotCallGateway()
    {
        var payment = Captured();
        payment.AddRefund(new PaymentRefund("k1", 40m, "R1", "COMPLETED"));
        RepoReturns(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentValidationException>(
            () => service.RefundAsync(Buyer, 1, 20m, "k2", CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_CapturesAndReportsProceeds()
    {
        RepoReturns(Authorized());
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentCaptureResult { CaptureId = "CAP1", Status = "COMPLETED", GrossAmount = 50m, PayPalFee = 2m, NetAmount = 48m });
        var service = CreateService();

        var view = await service.FulfilAsync(1, CancellationToken.None);

        Assert.Equal("Fulfilled", view.Status);
        Assert.Equal(50m, view.CapturedAmount);
        Assert.Equal(2m, view.PayPalFee);
        Assert.Equal(48m, view.NetAmount);
        await _gateway.DidNotReceive().ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WithStaleAuthorization_RenewsThenCaptures()
    {
        RepoReturns(Authorized(expiresAt: DateTimeOffset.UtcNow.AddDays(-1).ToString("O")));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentAuthorizationResult { PayPalOrderId = "", AuthorizationId = "AUTH2", Status = "CREATED", ExpiresAt = DateTimeOffset.UtcNow.AddDays(28).ToString("O"), Amount = 50m });
        _gateway.CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentCaptureResult { CaptureId = "CAP1", Status = "COMPLETED", GrossAmount = 50m, PayPalFee = 2m, NetAmount = 48m });
        var service = CreateService();

        var view = await service.FulfilAsync(1, CancellationToken.None);

        Assert.Equal("Fulfilled", view.Status);
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received().CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenStaleAuthorizationCannotBeRenewed_ReportsOperatorActionableError()
    {
        RepoReturns(Authorized(expiresAt: DateTimeOffset.UtcNow.AddDays(-1).ToString("O")));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PaymentGatewayException("REAUTHORIZATION_NOT_ALLOWED"));
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<AuthorizationNotRenewableException>(
            () => service.FulfilAsync(1, CancellationToken.None));
        Assert.Contains("could not be renewed", ex.Message);
    }

    [Fact]
    public async Task Cancel_AfterFulfilment_IsConflict()
    {
        RepoReturns(Captured());
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentConflictException>(() => service.CancelAsync(1, CancellationToken.None));
    }
}

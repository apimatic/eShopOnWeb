using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

/// <summary>
/// Runs the real <see cref="PaymentProcessingService"/> against a real (in-memory) catalog database and
/// a stand-in payment processor, so the money state machine - holds, captures, renewals, voids and
/// refunds - is exercised without depending on the sandbox or on the clock.
/// </summary>
public class PaymentServiceFixture
{
    public const string SHOPPER = "shopper@microsoft.com";
    public const string SOMEONE_ELSE = "other@microsoft.com";
    public const string USD = "USD";
    public const string AUTHORIZATION_ID = "AUTH-1";
    public const string RENEWED_AUTHORIZATION_ID = "AUTH-RENEWED";
    public const string PAYPAL_ORDER_ID = "PAYPAL-ORDER-1";
    public const string CAPTURE_ID = "CAPTURE-1";
    public const string VAULT_ID = "VAULT-1";
    public const string PAYPAL_CUSTOMER_ID = "CUSTOMER-1";
    public const string CARD_NUMBER = "4111111111111111";

    public decimal Fee = 1.42m;
    public CatalogContext Context { get; }
    public IPaymentGateway Gateway { get; } = Substitute.For<IPaymentGateway>();
    public PaymentProcessingService Service { get; }
    public TestTimeProvider Clock { get; } = new();

    /// <summary>Set to make the next authorize call fail; decremented per call.</summary>
    public Queue<Exception> FailNextAuthorizations { get; } = new Queue<Exception>();

    /// <summary>Set to make the next capture call fail; consumed per call.</summary>
    public Queue<Exception> FailNextCaptures { get; } = new Queue<Exception>();

    public PaymentServiceFixture()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
            .Options;
        Context = new CatalogContext(options);

        Gateway.Currency.Returns(USD);

        Gateway.AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (FailNextAuthorizations.Count > 0)
                {
                    return Task.FromException<PaymentAuthorization>(FailNextAuthorizations.Dequeue());
                }

                var request = call.Arg<AuthorizePaymentRequest>();
                return Task.FromResult(new PaymentAuthorization
                {
                    PayPalOrderId = PAYPAL_ORDER_ID,
                    AuthorizationId = AUTHORIZATION_ID,
                    Status = "CREATED",
                    ExpirationTime = Clock.Now.AddDays(29),
                    Amount = request.Amount,
                    Currency = request.Currency
                });
            });

        Gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<PaymentAuthorization>(new PaymentAuthorization
            {
                PayPalOrderId = PAYPAL_ORDER_ID,
                AuthorizationId = call.Arg<string>(),
                Status = "CREATED",
                ExpirationTime = Clock.Now.AddDays(29),
                Amount = 0m,
                Currency = USD
            }));

        Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (FailNextCaptures.Count > 0)
                {
                    return Task.FromException<CapturedPayment>(FailNextCaptures.Dequeue());
                }

                return Task.FromResult(new CapturedPayment
                {
                    CaptureId = CAPTURE_ID,
                    Status = "COMPLETED",
                    GrossAmount = call.Arg<decimal>(),
                    FeeAmount = Fee,
                    NetAmount = Math.Round(call.Arg<decimal>() - Fee, 2),
                    Currency = USD
                });
            });

        Gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new PaymentAuthorization
            {
                PayPalOrderId = string.Empty,
                AuthorizationId = RENEWED_AUTHORIZATION_ID,
                Status = "CREATED",
                ExpirationTime = Clock.Now.AddDays(29),
                Amount = call.Arg<decimal>(),
                Currency = USD
            }));

        Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new RefundedPayment
            {
                RefundId = "REFUND-PAYPAL-1",
                Status = "COMPLETED",
                Amount = call.Arg<decimal>(),
                FeeReturned = 0m,
                NetAmount = call.Arg<decimal>(),
                TotalRefunded = call.Arg<decimal>(),
                Currency = USD
            }));

        Gateway.SaveCardAsync(Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new SavedCardToken
            {
                VaultId = VAULT_ID,
                PayPalCustomerId = PAYPAL_CUSTOMER_ID,
                Brand = "VISA",
                Last4 = "1111",
                Expiry = "2030-11",
                CardHolderName = "Demo User",
                BillingCountry = "US"
            }));

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");

        Service = new PaymentProcessingService(
            new EfRepository<Order>(Context),
            new EfRepository<OrderPayment>(Context),
            new EfRepository<PaymentMethod>(Context),
            new EfRepository<CatalogItem>(Context),
            Gateway,
            uriComposer,
            Substitute.For<IAppLogger<PaymentProcessingService>>(),
            Clock);
    }

    public static CardDetails Card(string number = CARD_NUMBER, string expiry = "2030-11", string securityCode = "123")
        => new()
        {
            Number = number,
            Expiry = expiry,
            SecurityCode = securityCode,
            CardHolderName = "Demo User",
            Street = "1 Main St",
            City = "Portland",
            Region = "OR",
            PostalCode = "97201",
            CountryCode = "US"
        };

    public static Address Address() => new("1 Main St", "Portland", "OR", "US", "97201");

    public CatalogItem SeedItem(decimal price)
    {
        var item = new CatalogItem(1, 1, "description", $"item {price:0.00}", price, "pic.png");
        Context.CatalogItems.Add(item);
        Context.SaveChanges();
        return item;
    }

    public async Task<Order> PlaceOrder(params (decimal Price, int Quantity)[] lines)
    {
        var orderLines = lines.Select(line => new PlaceOrderLine(SeedItem(line.Price).Id, line.Quantity)).ToList();
        return await Service.PlaceOrderAsync(SHOPPER, orderLines, Address());
    }

    public Task<PaymentOperationResult> Pay(Order order, CardDetails? card = null, int? paymentMethodId = null)
        => Service.PayAsync(SHOPPER, order.Id, card ?? Card(), paymentMethodId);

    public async Task<PaymentMethod> SavedCard(string buyerId = SHOPPER)
        => await Service.SaveCardAsync(buyerId, Card(), null);

    public OrderPayment PaymentFor(int orderId) => Context.OrderPayments
        .Include(payment => payment.Refunds)
        .Single(payment => payment.OrderId == orderId);

    /// <summary>Makes the processor report the hold as gone (expired or released).</summary>
    public void HoldIsGone()
    {
        Gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<PaymentAuthorization>(new PaymentAuthorization
            {
                PayPalOrderId = PAYPAL_ORDER_ID,
                AuthorizationId = call.Arg<string>(),
                Status = "VOIDED",
                ExpirationTime = Clock.Now.AddDays(-1),
                Amount = 0m,
                Currency = USD
            }));
    }

    public void HoldCannotBeFound()
        => Gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<PaymentAuthorization?>(null));

    public void ReauthorizeIsRefused(string issue)
        => Gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromException<PaymentAuthorization>(
                new PaymentProcessorException("renewal refused", "UNPROCESSABLE_ENTITY", 422, new[] { issue })));

    public void CaptureIsRefusedOnce(string issue)
        => FailNextCaptures.Enqueue(new PaymentProcessorException("capture refused", "UNPROCESSABLE_ENTITY", 422,
            new[] { issue }));

    public void PayPalReports(IReadOnlyList<ProcessorTransactionLine> lines)
        => Gateway.ListTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(lines));
}

/// <summary>A clock the tests can move, so "stale" needs no waiting.</summary>
public class TestTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

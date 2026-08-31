using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using AuthorizationResult = Microsoft.eShopWeb.ApplicationCore.Models.Payments.AuthorizationResult;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total at PayPal (a hold on the money; nothing is captured yet).
/// Pays either with one-off card details or with one of the shopper's saved cards.
/// Idempotent: repeating the call for an already-authorized order returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;
    private readonly ILogger<PayOrderEndpoint> _logger;

    public PayOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IOptions<PayPalSettings> payPalSettings,
        ILogger<PayOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings.Value;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} not found." });
        }

        // Idempotent in effect: a repeated pay call returns the existing hold.
        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id));
        if (existingPayment is not null && existingPayment.AuthorizationId is not null)
        {
            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Payment = PaymentStateDto.FromEntity(existingPayment)
            });
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            return Results.Conflict(new { message = $"Order {order.Id} is {order.Status} and cannot be paid." });
        }

        PaymentSourceDto paymentSource;
        if (request.PaymentMethodId.HasValue)
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(request.PaymentMethodId.Value);
            if (savedCard is null || savedCard.BuyerId != request.BuyerId)
            {
                return Results.NotFound(new { message = $"Payment method {request.PaymentMethodId} not found." });
            }
            paymentSource = PaymentSourceDto.ForVaultedCard(savedCard.VaultTokenId);
        }
        else if (request.Card is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
            {
                return Results.BadRequest(new { message = "Card number and expiry (YYYY-MM) are required." });
            }
            paymentSource = PaymentSourceDto.ForCard(new CardDetails
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                HolderName = request.Card.Name,
                AddressLine1 = request.Card.BillingAddressLine1,
                AdminArea2 = request.Card.BillingCity,
                AdminArea1 = request.Card.BillingState,
                PostalCode = request.Card.BillingPostalCode,
                CountryCode = request.Card.BillingCountryCode
            });
        }
        else
        {
            return Results.BadRequest(new { message = "Provide either paymentMethodId or card details." });
        }

        AuthorizationResult authorization;
        try
        {
            authorization = await _paymentGateway.AuthorizeOrderAsync(order.Id, order.Total(),
                _payPalSettings.Currency, paymentSource, $"eshop-order-{order.Id}");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("PayPal authorization failed for order {OrderId}: {Error} {Issue} (debug {DebugId})",
                order.Id, ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PayPal authorization for order {OrderId} returned status {Status}",
                order.Id, authorization.Status);
            return Results.Problem(
                $"PayPal did not authorize the payment (status {authorization.Status}). " +
                "The payment requires an action this API cannot perform.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var payment = existingPayment ?? new OrderPayment(order.Id, order.BuyerId,
            authorization.PayPalOrderId, order.Total(), _payPalSettings.Currency);
        payment.SetAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);

        order.MarkPaymentAuthorized();

        if (existingPayment is null)
        {
            await _paymentRepository.AddAsync(payment);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment);
        }
        await _orderRepository.UpdateAsync(order);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentStateDto.FromEntity(payment)
        });
    }
}

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils the order and captures the authorized money. A stale
/// authorization is renewed first; one that can no longer be renewed fails with an
/// actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;

    public FulfilOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IPaymentGateway paymentGateway,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, user);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ClaimsPrincipal user)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(order.Id));
        if (payment is null)
        {
            return Results.Conflict($"Order {order.Id} has no payment; it must be paid before it can be fulfilled.");
        }

        // Idempotency: fulfilling an already-fulfilled order returns the recorded capture.
        if (order.Status == OrderStatus.Fulfilled)
        {
            response.OrderId = order.Id;
            response.OrderStatus = order.Status.ToString();
            response.Payment = PaymentDto.FromEntity(payment);
            return Results.Ok(response);
        }
        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            return Results.Conflict($"Order {order.Id} is {order.Status} and cannot be fulfilled.");
        }

        var currency = payment.Currency;

        // Renew the hold if it has gone stale before fulfilment.
        var authorization = await GetAuthorizationSafe(payment.AuthorizationId!);
        if (authorization is null || !IsCapturable(authorization))
        {
            GatewayAuthorizationDetails renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    payment.AuthorizationId!, payment.Amount, currency, $"eshop-payment-{payment.Reference}-reauthorize");
            }
            catch (PaymentGatewayException ex)
            {
                payment.MarkAuthorizationExpired();
                await _paymentRepository.UpdateAsync(payment);
                return Results.Conflict(
                    $"The PayPal authorization for order {order.Id} has expired and can no longer be renewed " +
                    $"({ex.Message}). Cancel the order and ask the shopper to place and pay for a new one.");
            }

            if (!IsCapturable(renewed))
            {
                payment.MarkAuthorizationExpired();
                await _paymentRepository.UpdateAsync(payment);
                return Results.Conflict(
                    $"The renewed PayPal authorization for order {order.Id} is {renewed.Status} and cannot be captured. " +
                    "Cancel the order and ask the shopper to place and pay for a new one.");
            }

            payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment);
        }

        GatewayCapture capture;
        try
        {
            capture = await _paymentGateway.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, currency, $"eshop-payment-{payment.Reference}-capture");
        }
        catch (PaymentGatewayException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.GatewayFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order);

        response.OrderId = order.Id;
        response.OrderStatus = order.Status.ToString();
        response.Payment = PaymentDto.FromEntity(payment);
        return Results.Ok(response);
    }

    private async Task<GatewayAuthorizationDetails?> GetAuthorizationSafe(string authorizationId)
    {
        try
        {
            return await _paymentGateway.GetAuthorizationAsync(authorizationId);
        }
        catch (PaymentGatewayException)
        {
            return null;
        }
    }

    private static bool IsCapturable(GatewayAuthorizationDetails authorization)
    {
        var statusOk = string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.Status, "PENDING", StringComparison.OrdinalIgnoreCase);
        var notExpired = !authorization.ExpirationTime.HasValue
            || authorization.ExpirationTime.Value > DateTimeOffset.UtcNow;
        return statusOk && notExpired;
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}

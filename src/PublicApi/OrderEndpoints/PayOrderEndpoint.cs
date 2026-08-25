using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequestBody
{
    public CardDetailsRequestDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public PayOrderRequest(int orderId, PayOrderRequestBody body, string buyerId)
    {
        OrderId = orderId;
        Body = body;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public PayOrderRequestBody Body { get; }
    public string BuyerId { get; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total with PayPal - either a one-off card, or one of the
/// shopper's saved cards. Does not take the money; that happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody body, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new PayOrderRequest(orderId, body, user.Identity!.Name!);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PaymentDependencies deps)
    {
        var response = new PayOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        var order = await deps.OrderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdAndBuyerSpec(request.OrderId, request.BuyerId));
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await deps.PaymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null)
        {
            return Results.Problem("This order has no associated payment record.", statusCode: 500);
        }

        // Idempotent in effect: a retried/double-clicked pay call after a prior success just
        // returns the existing authorization instead of authorizing the shopper again.
        if (payment.Status != PaymentStatus.AwaitingAuthorization)
        {
            return Results.Ok(ToResponse(response, order, payment));
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return Results.Conflict($"Order {order.Id} is not awaiting payment (status: {order.Status}).");
        }

        var hasCard = request.Body.Card != null;
        var hasPaymentMethod = request.Body.PaymentMethodId.HasValue;
        if (hasCard == hasPaymentMethod)
        {
            return Results.BadRequest("Provide exactly one of 'card' or 'paymentMethodId'.");
        }

        PayPalPaymentSource source;
        if (hasPaymentMethod)
        {
            var buyer = await deps.BuyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(request.BuyerId));
            var paymentMethod = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == request.Body.PaymentMethodId!.Value);
            if (paymentMethod == null)
            {
                return Results.NotFound($"Saved card {request.Body.PaymentMethodId} was not found.");
            }
            source = PayPalPaymentSource.FromVaultId(paymentMethod.CardId);
        }
        else
        {
            source = PayPalPaymentSource.FromCard(request.Body.Card!.ToPayPalCardDetails());
        }

        var idempotencyKey = $"order-{order.Id}-pay";

        PayPalAuthorizationResult authorization;
        try
        {
            authorization = await deps.PayPalClient.AuthorizeOrderAsync(payment.Amount, payment.Currency, source, idempotencyKey);
        }
        catch (PayPalApprovalRequiredException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: "PayPal buyer approval required");
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 402, title: ex.ErrorName ?? "Payment authorization failed");
        }

        payment.RecordAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        order.MarkPaymentAuthorized();

        await deps.PaymentRepository.UpdateAsync(payment);
        await deps.OrderRepository.UpdateAsync(order);

        return Results.Ok(ToResponse(response, order, payment));
    }

    private static PayOrderResponse ToResponse(PayOrderResponse response, Order order, Payment payment)
    {
        response.OrderStatus = order.Status.ToString();
        response.PayPalOrderId = payment.PayPalOrderId;
        response.AuthorizationId = payment.PayPalAuthorizationId;
        response.AuthorizationStatus = payment.AuthorizationStatus;
        response.AuthorizationExpiresAt = payment.AuthorizationExpiresAt;
        return response;
    }
}

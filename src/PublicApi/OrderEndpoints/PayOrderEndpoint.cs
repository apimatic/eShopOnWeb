using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, using either supplied card details (one-off) or one of the
/// shopper's saved cards. Idempotent in effect: a double-click never produces a second charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var orderId = http.GetRouteInt("orderId");
        if (orderId is null)
        {
            return Results.BadRequest("A valid order id is required.");
        }

        var hasCard = request.Card is not null;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            return Results.BadRequest("Provide exactly one of 'card' or 'savedPaymentMethodId'.");
        }

        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var gateway = http.RequestServices.GetRequiredService<IPaymentGatewayService>();
        var paymentLock = http.RequestServices.GetRequiredService<OrderPaymentLock>();

        using (await paymentLock.AcquireAsync(orderId.Value, http.RequestAborted))
        {
            var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId.Value));
            // Return 404 for both missing and not-owned so one shopper cannot probe another's orders.
            if (order is null || order.BuyerId != buyerId)
            {
                return Results.NotFound($"Order {orderId} was not found.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.Paid)
            {
                // Idempotent replay of a completed payment.
                return Results.Ok(new PayOrderResponse(request.CorrelationId())
                {
                    AlreadyPaid = true,
                    Order = OrderDto.FromEntity(order)
                });
            }

            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                return Results.BadRequest($"Order {orderId} has been refunded and cannot be paid.");
            }

            var amount = order.Total();
            if (amount <= 0m)
            {
                return Results.BadRequest("Order total must be greater than zero to take a payment.");
            }

            CardPaymentResult paymentResult;
            try
            {
                if (hasSaved)
                {
                    var savedCard = await http.RequestServices
                        .GetRequiredService<IRepository<SavedPaymentMethod>>()
                        .FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpecification(request.SavedPaymentMethodId!.Value, buyerId));
                    if (savedCard is null)
                    {
                        return Results.BadRequest($"Saved card {request.SavedPaymentMethodId} was not found.");
                    }

                    paymentResult = await gateway.ChargeSavedCardAsync(
                        new SavedCardChargeRequest(amount, OrderCurrency.Code, savedCard.PaymentTokenId), http.RequestAborted);
                }
                else
                {
                    if (!request.Card!.TryValidate(out var error))
                    {
                        return Results.BadRequest(error);
                    }

                    paymentResult = await gateway.ChargeCardAsync(
                        new CardChargeRequest(amount, OrderCurrency.Code, request.Card.ToCardDetails()), http.RequestAborted);
                }
            }
            catch (PaymentGatewayException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Payment failed");
            }

            order.MarkPaid(paymentResult.ProviderOrderId, paymentResult.CaptureId);
            await orderRepository.UpdateAsync(order);

            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                AlreadyPaid = false,
                Order = OrderDto.FromEntity(order)
            });
        }
    }
}

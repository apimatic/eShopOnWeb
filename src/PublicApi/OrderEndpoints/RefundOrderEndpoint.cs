using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds an order's payment in full via PayPal. Idempotent in effect: a double-click never
/// produces a second refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(http))
            .Produces<RefundOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
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

        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var gateway = http.RequestServices.GetRequiredService<IPaymentGatewayService>();
        var paymentLock = http.RequestServices.GetRequiredService<OrderPaymentLock>();

        using (await paymentLock.AcquireAsync(orderId.Value, http.RequestAborted))
        {
            var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId.Value));
            if (order is null || order.BuyerId != buyerId)
            {
                return Results.NotFound($"Order {orderId} was not found.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                // Idempotent replay of a completed refund.
                return Results.Ok(new RefundOrderResponse
                {
                    AlreadyRefunded = true,
                    Order = OrderDto.FromEntity(order)
                });
            }

            if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
            {
                return Results.BadRequest($"Order {orderId} is not in a paid state and cannot be refunded.");
            }

            RefundResult refundResult;
            try
            {
                refundResult = await gateway.RefundAsync(order.PaymentCaptureId!, http.RequestAborted);
            }
            catch (PaymentGatewayException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Refund failed");
            }

            order.MarkRefunded(refundResult.RefundId);
            await orderRepository.UpdateAsync(order);

            return Results.Ok(new RefundOrderResponse
            {
                AlreadyRefunded = false,
                Order = OrderDto.FromEntity(order)
            });
        }
    }
}

using System.Text.Json.Serialization;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order before fulfilment and releases the shopper's held
/// funds, so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, orderRepository, paymentService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepository, IOrderPaymentService paymentService)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null)
        {
            return Results.NotFound(new CancelOrderResponse { Message = $"Order {request.OrderId} was not found." });
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = order.Status.ToString() });
        }

        try
        {
            var payment = await paymentService.VoidPaymentAsync(order);
            return Results.Ok(new CancelOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Payment = payment == null ? null : OrderMapping.ToDto(payment)
            });
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new CancelOrderResponse { Message = ex.Message });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new CancelOrderResponse { Message = ex.Message });
        }
    }
}

public class CancelOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
    public string? Message { get; set; }
}

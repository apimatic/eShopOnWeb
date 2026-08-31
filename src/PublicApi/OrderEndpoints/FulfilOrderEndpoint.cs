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
/// Operator action: marks the order fulfilled and captures the held funds. Renews a stale
/// authorization before capturing.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderRepository, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepository, IOrderPaymentService paymentService)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null)
        {
            return Results.NotFound(new FulfilOrderResponse { Message = $"Order {request.OrderId} was not found." });
        }

        try
        {
            var payment = await paymentService.CapturePaymentAsync(order);
            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Payment = OrderMapping.ToDto(payment)
            });
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new FulfilOrderResponse { Message = ex.Message });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new FulfilOrderResponse { Message = ex.Message });
        }
    }
}

public class FulfilOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
    public string? Message { get; set; }
}

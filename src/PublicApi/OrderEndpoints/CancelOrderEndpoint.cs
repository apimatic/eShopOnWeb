using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing any held funds (no money moves).
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Cancels an order and releases held funds (operator)", Tags = new[] { "OrderEndpoints" })]
            async (int orderId, IPaymentService payments) =>
            {
                var order = await payments.CancelAsync(orderId);
                return Results.Ok(new CancelOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Payment = order.Payment is null ? null : OrderMapper.ToDto(order.Payment)
                });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

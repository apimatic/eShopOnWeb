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

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. A stale hold is renewed
/// rather than failing the fulfilment; a hold that can no longer be renewed reports an actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Fulfils an order and captures payment (operator)", Tags = new[] { "OrderEndpoints" })]
            async (int orderId, IPaymentService payments) =>
            {
                var order = await payments.FulfilAsync(orderId);
                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Payment = OrderMapper.ToDto(order.Payment!)
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

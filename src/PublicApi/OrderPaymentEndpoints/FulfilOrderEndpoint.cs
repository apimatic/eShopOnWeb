using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. Renews a stale hold if
/// needed; reports if the hold can no longer be renewed. Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentProcessingService service) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentProcessingService service)
    {
        var order = await service.FulfilAsync(request.OrderId);
        return Results.Ok(OrderPaymentMapping.ToResponse(order));
    }
}

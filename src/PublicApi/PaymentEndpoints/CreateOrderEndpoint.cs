using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Places an order from catalog items. The order starts awaiting payment.</summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateOrderRequest request, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
                {
                    return await HandleAsync(request, paymentService, user);
                })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        var order = await paymentService.CreateOrderAsync(buyerId!, request);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Order = order
        };

        return Results.Created($"api/orders/{order.OrderId}", response);
    }
}

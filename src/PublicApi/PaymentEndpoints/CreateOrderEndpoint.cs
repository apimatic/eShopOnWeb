using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public CreateOrderEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CreateOrderRequest request, CancellationToken cancellationToken) =>
                    await HandleAsync(request, user, cancellationToken))
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var order = await _paymentService.CreateOrderAsync(buyerId, request, cancellationToken);

        return Results.Created($"api/orders/{order.OrderId}", new CreateOrderResponse
        {
            OrderId = order.OrderId,
            Order = order
        });
    }
}

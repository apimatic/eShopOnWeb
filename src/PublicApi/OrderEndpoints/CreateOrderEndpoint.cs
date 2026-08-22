using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                return await HandleAsync(request, user, service);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, new ClaimsPrincipal(), service);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = RequireBuyerId(user);
        var lines = (request.Items ?? Enumerable.Empty<OrderLineDto>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(buyerId, lines, OrderApiMapper.ToAddress(request.ShippingAddress));
        var response = OrderApiMapper.ToResponse(order);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }

    internal static string RequireBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        return name;
    }
}

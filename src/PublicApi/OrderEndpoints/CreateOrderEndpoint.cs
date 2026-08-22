using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(BindBuyer(request, httpContext), checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = request.BuyerId!;
        Address? address = null;
        if (request.ShipToAddress != null)
        {
            address = new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);
        }

        var items = request.Items.Select(i => new PlaceOrderItem
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        var order = await checkout.PlaceOrderAsync(buyerId, items, address);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static CreateOrderRequest BindBuyer(CreateOrderRequest request, HttpContext httpContext)
    {
        request.BuyerId = BuyerId(httpContext);
        return request;
    }

    internal static string BuyerId(HttpContext httpContext) =>
        httpContext.User.Identity?.Name
        ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
        ?? throw new ApplicationCore.Exceptions.CheckoutException(401, "The caller is not authenticated.");
}

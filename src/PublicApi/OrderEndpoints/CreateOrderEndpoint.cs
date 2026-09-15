using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext http, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(request, checkout, http.User);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout) =>
        HandleAsync(request, checkout, ClaimsPrincipal.Current ?? new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var items = (request.Items ?? []).Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        PlaceOrderAddress? shipTo = request.ShipTo == null
            ? null
            : new PlaceOrderAddress(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);

        var order = await checkout.PlaceOrderAsync(buyerId, items, shipTo);
        var body = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponse.From(order, string.Empty)
        };
        body.Order.Currency = order.Currency ?? body.Order.Currency;
        return Results.Created($"api/orders/{order.Id}", body);
    }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

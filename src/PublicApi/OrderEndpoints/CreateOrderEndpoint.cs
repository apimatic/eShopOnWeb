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
    private readonly IPayPalGateway _payPal;

    public CreateOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderCheckoutService checkoutService) =>
            {
                request.BuyerId = user.RequireBuyerId();
                return await HandleAsync(request, checkoutService);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkoutService)
    {
        Address? address = null;
        if (request.ShipTo != null)
        {
            address = new Address(
                request.ShipTo.Street ?? "123 Main St.",
                request.ShipTo.City ?? "Seattle",
                request.ShipTo.State ?? "WA",
                request.ShipTo.Country ?? "US",
                request.ShipTo.ZipCode ?? "98101");
        }

        var lines = (request.Items ?? new System.Collections.Generic.List<CreateOrderItemRequest>()).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkoutService.PlaceOrderAsync(request.BuyerId!, lines, address);
        var response = OrderResponseMapper.ToResponse(order, _payPal.Currency, request.CorrelationId());
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

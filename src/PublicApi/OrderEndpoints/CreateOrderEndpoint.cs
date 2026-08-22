using System.Collections.Generic;
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
                return await HandleAsync(request, checkout, httpContext.User);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout) =>
        HandleAsync(request, checkout, null);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal? buyer)
    {
        var buyerId = buyer?.Identity?.Name
            ?? throw new ApplicationCore.Exceptions.PaymentException("The caller identity is missing.", 401);

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

        var lines = (request.Items ?? new List<CreateOrderItemRequest>()).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(buyerId, lines, address);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

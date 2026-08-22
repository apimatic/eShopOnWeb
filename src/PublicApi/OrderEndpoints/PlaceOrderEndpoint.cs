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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderApiRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderApiRequest request, HttpContext http, ICheckoutPaymentService service) =>
                await HandleAsync(request, http, service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderApiRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(PlaceOrderApiRequest request, HttpContext http, ICheckoutPaymentService service)
    {
        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var items = request.Items.ConvertAll(i => new PlaceOrderItem
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        });

        var order = await service.PlaceOrderAsync(new PlaceOrderRequest
        {
            BuyerId = http.RequireBuyerId(),
            Items = items,
            ShipTo = shipTo
        });

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = OrderResponse.From(order).Items
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

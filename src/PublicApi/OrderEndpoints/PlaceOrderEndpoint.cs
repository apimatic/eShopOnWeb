using System;
using System.Linq;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IShopperOrderService service, HttpContext http) =>
            {
                return await HandleAsync(request, service, http);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service, HttpContext http)
    {
        var buyerId = CallerIdentity.RequireBuyerId(http);
        var address = new Address(
            string.IsNullOrWhiteSpace(request.Street) ? "123 Main St." : request.Street,
            string.IsNullOrWhiteSpace(request.City) ? "Kent" : request.City,
            string.IsNullOrWhiteSpace(request.State) ? "WA" : request.State,
            string.IsNullOrWhiteSpace(request.Country) ? "USA" : request.Country,
            string.IsNullOrWhiteSpace(request.ZipCode) ? "98042" : request.ZipCode);

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(buyerId, lines, address, http.RequestAborted);
        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = order.Id };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

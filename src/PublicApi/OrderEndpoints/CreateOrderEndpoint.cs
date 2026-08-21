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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService orders, HttpContext httpContext) =>
            {
                return await HandleAsync(request, orders, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orders, HttpContext httpContext)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        Address? shipTo = null;
        if (request.ShipTo != null)
        {
            shipTo = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var items = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await orders.PlaceOrderAsync(httpContext.RequireBuyerId(), items, shipTo, httpContext.RequestAborted);
        response.OrderId = order.Id;
        response.Order = OrderDtoMapper.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orders)
        => HandleAsync(request, orders, default!);
}

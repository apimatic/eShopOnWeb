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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(request, service, httpContext.GetBuyerId()!);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, string buyerId)
    {
        var lines = (request.Items ?? new()).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        Address? shipTo = request.ShipTo is null
            ? null
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await service.PlaceOrderAsync(buyerId, lines, shipTo, default);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

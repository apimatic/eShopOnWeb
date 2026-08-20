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
    private readonly IOrderNotificationService _notifications;

    public PlaceOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IShopperOrderService service, HttpContext httpContext) =>
            {
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service)
        => HandleAsync(request, service, new DefaultHttpContext());

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service, HttpContext httpContext)
    {
        try
        {
            var buyerId = httpContext.GetRequiredBuyerId();
            Address? address = null;
            if (request.ShippingAddress is not null)
            {
                var a = request.ShippingAddress;
                address = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);
            }

            var lines = (request.Items ?? Enumerable.Empty<PlaceOrderItemRequest>())
                .Select(i => new OrderLineRequest { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
                .ToList();

            var order = await service.PlaceAsync(buyerId, lines, address);
            await _notifications.NotifyOrderPlacedAsync(order.Id, order.BuyerId);

            var response = new PlaceOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

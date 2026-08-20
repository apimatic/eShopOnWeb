using System.Collections.Generic;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderSmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext httpContext, IOrderSmsService service) =>
            {
                var lines = (request.Items ?? new List<PlaceOrderItemRequest>())
                    .Select(i => new CatalogLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                Address? address = null;
                if (request.ShipToAddress != null)
                {
                    address = new Address(
                        request.ShipToAddress.Street ?? string.Empty,
                        request.ShipToAddress.City ?? string.Empty,
                        request.ShipToAddress.State ?? string.Empty,
                        request.ShipToAddress.Country ?? string.Empty,
                        request.ShipToAddress.ZipCode ?? string.Empty);
                }

                var result = await service.PlaceOrderAsync(httpContext.GetRequiredBuyerId(), lines, address);
                return result.ToHttpResult(order =>
                {
                    var response = new PlaceOrderResponse
                    {
                        OrderId = order.Id,
                        Status = order.Status.ToString(),
                        Total = order.Total()
                    };
                    return Results.Created($"api/my-orders", response);
                });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderSmsService orderSmsService)
        => Task.FromResult(Results.Ok());
}

public class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public PlaceOrderAddressRequest? ShipToAddress { get; set; }
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

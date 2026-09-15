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

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public PlaceOrderAddressRequest? ShipTo { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPlacementService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPlacementService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPlacementService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetRequiredUserName();
        Address? address = null;
        if (request.ShipTo is not null)
        {
            address = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var items = request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceAsync(buyerId, items, address);
        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

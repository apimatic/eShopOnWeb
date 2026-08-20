using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? Address { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderLifecycleService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext http, IOrderLifecycleService service) =>
            {
                return await HandleAsync(request, http, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderLifecycleService service)
        => HandleAsync(request, http: null!, service);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http, IOrderLifecycleService service)
    {
        var buyerId = http.RequireBuyerId();
        Address? address = null;
        if (request.Address != null
            && !string.IsNullOrWhiteSpace(request.Address.Street)
            && !string.IsNullOrWhiteSpace(request.Address.City)
            && !string.IsNullOrWhiteSpace(request.Address.Country)
            && !string.IsNullOrWhiteSpace(request.Address.ZipCode))
        {
            address = new Address(
                request.Address.Street,
                request.Address.City,
                request.Address.State,
                request.Address.Country,
                request.Address.ZipCode);
        }

        var items = request.Items
            .Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(buyerId, items, address);
        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderNotificationService service, HttpContext httpContext) =>
            {
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService orderNotificationService)
        => HandleAsync(request, orderNotificationService, null!);

    private Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, HttpContext httpContext)
    {
        return EndpointHelpers.ExecuteAsync(async () =>
        {
            var buyerId = httpContext.User.RequireBuyerId();
            var lines = (request.Items ?? new List<CreateOrderItemRequest>())
                .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                .ToList();

            ShippingAddressDto? address = request.ShipToAddress == null
                ? null
                : new ShippingAddressDto(
                    request.ShipToAddress.Street,
                    request.ShipToAddress.City,
                    request.ShipToAddress.State,
                    request.ShipToAddress.Country,
                    request.ShipToAddress.ZipCode);

            var order = await service.PlaceOrderAsync(buyerId, lines, address);
            var response = new CreateOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

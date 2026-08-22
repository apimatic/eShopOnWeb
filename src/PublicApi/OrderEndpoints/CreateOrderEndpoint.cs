using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IShopperOrderNotificationService service, CancellationToken cancellationToken) =>
            {
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

                var lines = (request.Items ?? new List<CreateOrderItem>()).Select(i =>
                    new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();

                var order = await service.PlaceOrderAsync(user.GetBuyerId(), lines, address, cancellationToken);
                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.FulfillmentStatus.ToString()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderNotificationService service)
        => Task.FromResult(Results.Ok());
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShipToAddressDto? ShipTo { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

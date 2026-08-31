using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities for the authenticated shopper, reusing the
/// app's existing order/order-item model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPlacementService orderPlacementService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, orderPlacementService, user);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IOrderPlacementService orderPlacementService,
        ClaimsPrincipal user)
    {
        if (request?.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every order line must have a quantity of at least one.");
        }

        var buyerId = user.GetBuyerId();
        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();
        var address = ToAddress(request.ShipToAddress);

        var orderId = await orderPlacementService.PlaceOrderAsync(buyerId, lines, address);

        var response = new CreateOrderResponse(request.CorrelationId()) { OrderId = orderId };
        return Results.Created($"api/orders/{orderId}", response);
    }

    private static Address ToAddress(AddressRequest? address)
    {
        if (address is null)
        {
            // Shipping is not billed; a placeholder keeps the reused Order model valid when omitted.
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "N/A" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City,
            address.State ?? "N/A",
            string.IsNullOrWhiteSpace(address.Country) ? "N/A" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    /// <summary>The identifier of the newly placed order.</summary>
    public int OrderId { get; set; }
}

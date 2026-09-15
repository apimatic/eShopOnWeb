using System;
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

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's order/order-item
/// model. The shopper is told their order was placed. Returns the new order's id.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, System.Security.Claims.ClaimsPrincipal user, IShopOrderService orderService) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }
                request.OwnerId = owner;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IShopOrderService orderService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLineItem { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var result = await orderService.PlaceOrderAsync(request.OwnerId, lines, address);
        if (result.Error is not null || result.Order is null)
        {
            return Results.BadRequest(result.Error ?? "The order could not be placed.");
        }

        response.OrderId = result.Order.Id;
        response.Status = result.Order.Status.ToString();
        return Results.Created($"api/orders/{result.Order.Id}", response);
    }

    private static Address BuildAddress(AddressRequest? a)
    {
        // Ship-to is optional on this API; the underlying order model requires an address, so fall back
        // to a placeholder when the caller does not supply one.
        if (a is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            Blank(a.Street), Blank(a.City), a.State ?? "N/A", Blank(a.Country), Blank(a.ZipCode));

        static string Blank(string? v) => string.IsNullOrWhiteSpace(v) ? "N/A" : v!;
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }

    /// <summary>The signed-in shopper; set from the token, ignored if supplied by the caller.</summary>
    public string OwnerId { get; set; } = string.Empty;
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
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the placed order, returned as a top-level field.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
}

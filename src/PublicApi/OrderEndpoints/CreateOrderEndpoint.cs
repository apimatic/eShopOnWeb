using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A single requested order line: which catalog item, and how many.</summary>
public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Places an order from catalog items. The buyer comes from the token, not the body.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
}

/// <summary>
/// Places a new order from catalog items for the authenticated shopper, reusing the app's existing order
/// model. Returns the new order's id.
/// </summary>
public class CreateOrderEndpoint : InvoiceEndpointBase, IEndpoint
{
    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPlacementService orderPlacementService) =>
            {
                var buyerId = CurrentUserName;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var lines = (request.Items ?? new List<CreateOrderItemDto>())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var orderId = await orderPlacementService.PlaceOrderAsync(buyerId, lines, RequestAborted);

                var response = new CreateOrderResponse(request.CorrelationId()) { OrderId = orderId };
                return Results.Created($"api/orders/{orderId}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

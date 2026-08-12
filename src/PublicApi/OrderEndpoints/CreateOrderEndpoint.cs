using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderLineRequest> Items { get; set; } = new();

    internal string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the created order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids + quantities, reusing the app's
/// existing order/order-item model, and tells the shopper their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderNotificationService service) =>
            {
                request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<OrderLineRequest>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await service.PlaceOrderAsync(request.BuyerId, lines);
        if (result.Status == ResultStatus.Invalid)
        {
            return Results.ValidationProblem(result.ValidationErrors.ToDictionary(
                e => string.IsNullOrEmpty(e.Identifier) ? "items" : e.Identifier,
                e => new[] { e.ErrorMessage }));
        }

        var order = result.Value;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

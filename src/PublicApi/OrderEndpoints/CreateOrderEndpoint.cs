using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                request.CallerId = user.GetCallerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<CreateOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(request.CallerId, lines, ct);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<CreateOrderItem>? Items { get; set; }

    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    /// <summary>The identifier of the order that was placed.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
}

using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the created order (top-level, so callers can drive the flow).</summary>
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService service,
             IPaymentSettings settings, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
                var order = await service.PlaceOrderAsync(buyerId, lines, ct);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Currency = settings.Currency,
                    PaymentStatus = "AwaitingPayment"
                });
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    // Routing and the caller identity are handled in AddRoute; this interface member is unused.
    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService service) =>
        Task.FromResult(Results.Empty as IResult);
}

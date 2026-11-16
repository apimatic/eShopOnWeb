using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public PaymentAddressDto? ShipToAddress { get; set; }
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Places an order from catalog item ids and quantities. The order starts awaiting payment.
/// </summary>
public sealed class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                    var lines = request.Items.Select(i => new CreateOrderLine
                    {
                        CatalogItemId = i.CatalogItemId,
                        Quantity = i.Quantity
                    }).ToList();

                    var order = await paymentService.CreateOrderAsync(buyerId, lines,
                        request.ShipToAddress?.ToPaymentAddress(), cancellationToken);

                    return Results.Created($"api/orders/{order.OrderId}", order);
                });
            })
            .Produces<OrderInfo>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }
}

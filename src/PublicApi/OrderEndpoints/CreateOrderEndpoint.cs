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
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper from catalog items and quantities, reusing the app's
/// existing order/order-item model. The caller's identity (the order owner) comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(request, service, user, cancellationToken))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var lines = (request.Items ?? new List<CreateOrderItem>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await service.PlaceOrderAsync(buyerId, lines, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }

        var placed = result.Value!;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = placed.OrderId,
            Total = placed.Total,
            Currency = placed.Currency,
            ItemCount = placed.ItemCount
        };
        return Results.Created($"api/orders/{placed.OrderId}", response);
    }
}

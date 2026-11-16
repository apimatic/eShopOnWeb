using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Place an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderSummary>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        var items = (request.Items ?? new System.Collections.Generic.List<CreateOrderItemRequest>())
            .Select(i => new OrderLineInput { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var summary = await paymentService.CreateOrderAsync(
            request.BuyerId ?? string.Empty,
            items,
            request.ShippingAddress?.ToAddress());

        return Results.Created($"api/orders/{summary.OrderId}", summary);
    }
}

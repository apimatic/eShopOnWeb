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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var buyerId = GetBuyerId(user);
        var shipTo = new Address(
            request.ShipToAddress.Street,
            request.ShipToAddress.City,
            request.ShipToAddress.State,
            request.ShipToAddress.Country,
            request.ShipToAddress.ZipCode);

        var order = await paymentService.CreateOrderAsync(
            buyerId,
            request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList(),
            shipTo);

        response.OrderId = order.OrderId;
        response.Status = order.Status;
        response.Total = order.Total;
        response.Currency = order.Currency;
        response.Items = order.Items;

        return Results.Created($"api/orders/{order.OrderId}", response);
    }

    internal static string GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value
        ?? user.Identity?.Name
        ?? throw new System.UnauthorizedAccessException("The token does not carry a shopper identity.");
}

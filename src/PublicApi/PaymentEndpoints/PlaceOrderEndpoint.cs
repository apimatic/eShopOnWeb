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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Places an order from catalog items; it starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.Require(user);

        if (request.ShipToAddress is null)
            throw new PaymentValidationException("A shipping address is required to place an order.");
        if (request.Items is null || request.Items.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");

        var lines = request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();

        var placed = await service.PlaceOrderAsync(buyerId, lines, request.ShipToAddress.ToAddress(), CancellationToken.None);

        var response = new PlaceOrderResponse
        {
            OrderId = placed.OrderId,
            Status = "AwaitingPayment",
            Amount = placed.Amount,
            Currency = placed.Currency,
        };
        return Results.Created($"api/orders/{placed.OrderId}", response);
    }
}

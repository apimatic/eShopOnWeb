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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order reuses
/// the app's existing Order/OrderItem model and starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.Caller = CallerContext.From(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var a = request.ShipToAddress;
        var address = new Address(
            street: string.IsNullOrWhiteSpace(a?.Street) ? "N/A" : a!.Street!,
            city: string.IsNullOrWhiteSpace(a?.City) ? "N/A" : a!.City!,
            state: a?.State ?? string.Empty,
            country: string.IsNullOrWhiteSpace(a?.Country) ? "N/A" : a!.Country!,
            zipcode: string.IsNullOrWhiteSpace(a?.ZipCode) ? "00000" : a!.ZipCode!);

        var placed = await paymentService.PlaceOrderAsync(request.Caller.Username, lines, address);

        var response = new CreateOrderResponse
        {
            OrderId = placed.OrderId,
            Status = "AwaitingPayment",
            Amount = placed.Amount,
            Currency = placed.Currency
        };
        return Results.Created($"api/orders/{placed.OrderId}", response);
    }
}

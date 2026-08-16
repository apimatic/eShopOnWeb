using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order reuses
/// the app's existing order/order-item model and starts life awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.CallerBuyerId = user.GetBuyerId();
                request.CallerIsAdmin = user.IsAdministrator();
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? null
            : new ShippingAddressRequest(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var orderId = await service.PlaceOrderAsync(request.CallerBuyerId, lines, shipTo);

        var order = await service.GetOrderForCallerAsync(orderId, request.CallerBuyerId, request.CallerIsAdmin);
        var response = new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = order?.Status.ToString() ?? "AwaitingPayment",
            Total = order?.Total() ?? 0m,
            Currency = service.Currency
        };

        return Results.Created($"api/orders/{orderId}", response);
    }
}

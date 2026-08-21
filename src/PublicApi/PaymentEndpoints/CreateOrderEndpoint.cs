using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequestDto request, IPaymentService paymentService, PayPalSettings settings, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);

                var lines = request.Items
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var shipTo = request.ShipToAddress is null
                    ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
                    : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                        request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var order = await paymentService.PlaceOrderAsync(buyerId, lines, shipTo, ct);

                var response = new CreateOrderResponseDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Total = order.Total(),
                    Currency = settings.Currency
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponseDto>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}

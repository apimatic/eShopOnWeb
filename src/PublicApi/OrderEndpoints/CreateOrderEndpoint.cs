using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places a new order from catalog items for the signed-in shopper. The order starts awaiting
/// payment; no money moves until it is paid.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, PayPalOptions options, CancellationToken ct) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
                var address = (request.ShipToAddress ?? new ShipToAddressRequest()).ToAddress();

                var order = await paymentService.PlaceOrderAsync(buyerId, lines, address, ct);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Currency = options.CurrencyCode,
                    PaymentStatus = "AwaitingPayment",
                    Items = order.OrderItems.Select(oi => new CreateOrderLineView
                    {
                        CatalogItemId = oi.ItemOrdered.CatalogItemId,
                        ProductName = oi.ItemOrdered.ProductName,
                        UnitPrice = oi.UnitPrice,
                        Units = oi.Units
                    }).ToList()
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Amounts come from catalog prices; the caller's identity comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public PlaceOrderEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetUserName() ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request)
    {
        var items = (request.Items ?? new()).Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var shipping = request.Shipping is null
            ? null
            : new ShippingAddressInput(request.Shipping.Street, request.Shipping.City, request.Shipping.State,
                request.Shipping.Country, request.Shipping.ZipCode);

        var result = await _paymentService.PlaceOrderAsync(request.BuyerId, items, shipping);
        return ToHttp(result, placed => Results.Created($"api/orders/{placed.OrderId}", new PlaceOrderResponse
        {
            OrderId = placed.OrderId,
            Status = placed.Status,
            Amount = placed.Amount,
            Currency = placed.Currency,
            PaymentReference = placed.PaymentReference
        }));
    }
}

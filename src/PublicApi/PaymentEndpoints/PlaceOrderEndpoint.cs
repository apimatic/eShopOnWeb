using System.Collections.Generic;
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

/// <summary>POST /api/orders — place an order from catalog items; it starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
                await HandleAsync(request, paymentService, user))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = PaymentApi.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var result = await paymentService.PlaceOrderAsync(buyerId, lines, address);
        return PaymentApi.ToHttp(result, orderId => new { orderId, status = OrderStatus.AwaitingPayment.ToString() });
    }

    private static Address BuildAddress(ShipToAddressDto? dto) => new(
        street: Fallback(dto?.Street, "N/A"),
        city: Fallback(dto?.City, "N/A"),
        state: Fallback(dto?.State, "N/A"),
        country: Fallback(dto?.Country, "N/A"),
        zipcode: Fallback(dto?.ZipCode, "00000"));

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}

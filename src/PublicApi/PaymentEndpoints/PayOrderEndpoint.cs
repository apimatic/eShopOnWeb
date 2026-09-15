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
/// Authorizes the order total: places a hold on the money without taking it. Pays with one-off card
/// details or one of the shopper's saved cards. POST /api/orders/{orderId}/pay
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;

    public PayOrderEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) => await HandleAsync(orderId, request, user))
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var instrument = new PaymentInstrument
        {
            Card = request.Card?.ToCardDetails(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        var payment = await _service.AuthorizeAsync(buyerId, orderId, instrument);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = orderId,
            PaymentStatus = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            Amount = payment.Amount,
            Currency = payment.Currency
        });
    }
}

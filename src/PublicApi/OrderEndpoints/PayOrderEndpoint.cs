using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes an order's total: places a hold on the money without taking it. The shopper pays with
/// raw card details for a one-off payment, or names one of their saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
        => HandleAsync(request, user, service, default);

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = user.BuyerId();

        var instrument = new PaymentInstrument(
            request.Card?.ToCardPaymentDetails(),
            request.SavedCardId);

        var payment = await service.AuthorizeAsync(buyerId, request.OrderId, instrument, ct);
        return Results.Ok(PaymentDto.From(payment));
    }
}

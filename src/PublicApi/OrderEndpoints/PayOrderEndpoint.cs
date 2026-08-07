using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays for an order with PayPal, using either one-off card details or one of the shopper's saved
/// cards. Idempotent in effect: a double-submit never results in a double charge.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http);
            })
            .Produces<PaymentStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var paymentService = http.RequestServices.GetRequiredService<IPaymentService>();

        var hasCard = request.Card is not null;
        var hasSaved = request.PaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            return Results.BadRequest(new { message = "Provide either 'card' details or a 'paymentMethodId', but not both." });
        }

        var result = await paymentService.PayOrderAsync(
            buyerId,
            request.OrderId,
            request.Card?.ToDomain(),
            request.PaymentMethodId,
            http.RequestAborted);

        return Results.Ok(PaymentStatusResponse.From(result, request.CorrelationId()));
    }
}

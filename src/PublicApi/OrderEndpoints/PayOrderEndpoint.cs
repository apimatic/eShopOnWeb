using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PayOrderRequest(
    int OrderId,
    string? SavedCardPaymentMethodId,
    string? CardNumber,
    string? CardExpiry,
    string? CardCvv,
    string? CardHolderName);

public record PayOrderResponse(int OrderId, string PayPalOrderId, string? AuthorizationId, string? AuthorizationStatus);

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IPaymentService svc, IHttpContextAccessor ctx) =>
            {
                var req = request with { OrderId = orderId };
                return await HandleAsync(req, svc, ctx);
            })
            .Produces<PayOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService svc, IHttpContextAccessor ctx)
    {
        var buyerId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        var input = new PaymentInput
        {
            SavedCardId = request.SavedCardPaymentMethodId,
            CardNumber = request.CardNumber,
            CardExpiry = request.CardExpiry,
            CardCvv = request.CardCvv,
            CardHolderName = request.CardHolderName,
            BillingCountryCode = "US"
        };
        var payment = await svc.AuthorizePaymentAsync(request.OrderId, buyerId, input);

        return Results.Ok(new PayOrderResponse(request.OrderId, payment.PayPalOrderId, payment.AuthorizationId, payment.AuthorizationStatus));
    }
}

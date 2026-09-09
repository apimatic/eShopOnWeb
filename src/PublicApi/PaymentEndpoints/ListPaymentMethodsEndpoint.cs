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
/// GET /api/payment-methods — the caller's saved cards, described safely. Shopper-scoped: only the
/// caller's own cards are returned.
/// </summary>
public class ListPaymentMethodsEndpoint : PaymentEndpointBase, IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService) =>
                await HandleAsync(new ListPaymentMethodsRequest(), paymentService))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var cards = await paymentService.GetSavedCardsAsync(BuyerId, RequestAborted);
        return Results.Ok(new { paymentMethods = cards });
    }
}

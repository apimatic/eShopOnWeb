using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// GET /api/payment-methods — the caller's own saved cards (safe descriptions only). Shopper-scoped.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService service) =>
                await HandleAsync(service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentMethodService service)
    {
        var buyerId = CurrentUser.RequireBuyerId(_http);
        var cards = await service.ListAsync(buyerId, CurrentUser.RequestAborted(_http));
        return Results.Ok(ListPaymentMethodsResponse.From(cards));
    }
}

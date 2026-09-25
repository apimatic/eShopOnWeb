using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, EmptyRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service) => await HandleAsync(new EmptyRequest(), service))
            .Produces<IReadOnlyList<SavedCardInfo>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, ISavedCardService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);
        var cards = await service.ListAsync(buyerId, ctx.RequestAborted);
        return Results.Ok(cards);
    }
}

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// GET /api/payment-methods — the caller's saved cards (safe descriptors only). Shopper-scoped.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public ListPaymentMethodsEndpoint(IPaymentMethodService paymentMethodService) => _paymentMethodService = paymentMethodService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(new ListPaymentMethodsRequest(user.GetUserName() ?? string.Empty)))
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request)
    {
        var result = await _paymentMethodService.ListCardsAsync(request.BuyerId);
        return ToHttp(result, cards => Results.Ok(cards));
    }
}

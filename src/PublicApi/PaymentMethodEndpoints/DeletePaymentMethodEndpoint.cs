using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes one of the caller's saved cards. Afterwards it
/// no longer appears among the caller's cards and can no longer be used to pay. Shopper-scoped.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public DeletePaymentMethodEndpoint(IPaymentMethodService paymentMethodService) => _paymentMethodService = paymentMethodService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
                await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId, user.GetUserName() ?? string.Empty)))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var result = await _paymentMethodService.DeleteCardAsync(request.BuyerId, request.PaymentMethodId);
        return ToHttp(result, Results.NoContent());
    }
}

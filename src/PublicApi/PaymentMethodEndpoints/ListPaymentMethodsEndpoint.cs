using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>The caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, PaymentMethodOperationRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(new PaymentMethodOperationRequest { BuyerId = user.Identity?.Name, Cancellation = ct }, service);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodOperationRequest request, IPaymentMethodService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await service.GetForBuyerAsync(request.BuyerId, request.Cancellation);
        return Results.Ok(methods.Select(SavedPaymentMethodResponse.From).ToList());
    }
}

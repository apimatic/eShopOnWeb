using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record DeletePaymentMethodRequest(string PaymentMethodId);

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPayPalPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string paymentMethodId, IPayPalPaymentService payPal) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), payPal);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPayPalPaymentService payPal)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var user = httpCtx?.User;
        var userId = user?.FindFirstValue(ClaimTypes.Email)
                  ?? user?.FindFirstValue("sub")
                  ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        try
        {
            var tokens = await payPal.ListVaultedTokensAsync(userId, ct);
            var owned = tokens.Any(t => t.TokenId == request.PaymentMethodId);
            if (!owned)
                return Results.NotFound();

            await payPal.DeleteVaultedTokenAsync(request.PaymentMethodId, ct);
            return Results.NoContent();
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<UserPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId,
                   HttpContext httpContext,
                   IRepository<UserPaymentMethod> pmRepo,
                   IPayPalClient paypal,
                   ILogger<DeletePaymentMethodEndpoint> logger) =>
            {
                var userName = httpContext.User.Identity!.Name!;
                var spec = new UserPaymentMethodByIdAndUserIdSpec(paymentMethodId, userName);
                var pm = await pmRepo.FirstOrDefaultAsync(spec);
                if (pm == null)
                    return Results.NotFound(new { error = "Payment method not found." });

                // Delete from PayPal vault
                try
                {
                    await paypal.DeleteVaultPaymentTokenAsync(pm.PaymentTokenId);
                }
                catch (PayPalException ex)
                {
                    logger.LogWarning(ex, "PayPal vault delete failed for token {TokenId}, soft-deleting locally", pm.PaymentTokenId);
                    // Continue with soft-delete even if PayPal delete fails
                }

                pm.MarkDeleted();
                await pmRepo.UpdateAsync(pm);

                return Results.NoContent();
            })
            .Produces(204)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<UserPaymentMethod> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class DeletePaymentMethodRequest : BaseRequest { }

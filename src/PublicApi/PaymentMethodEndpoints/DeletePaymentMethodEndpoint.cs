using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IRepository<Buyer> buyerRepo,
                   IPayPalService paypal, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var buyer = await buyerRepo.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), ct);
                if (buyer == null) return Results.NotFound("No payment methods found.");

                var pm = buyer.FindPaymentMethod(paymentMethodId);
                if (pm == null) return Results.NotFound($"Payment method {paymentMethodId} not found.");

                var vaultId = pm.CardId;
                if (!string.IsNullOrEmpty(vaultId))
                    await paypal.DeleteCardAsync(vaultId, ct);

                buyer.RemovePaymentMethod(paymentMethodId);
                await buyerRepo.UpdateAsync(buyer, ct);

                return Results.NoContent();
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}

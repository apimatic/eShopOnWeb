using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalPaymentService paypal,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId);
                var method = await pmRepo.FirstOrDefaultAsync(spec, ct);
                if (method == null) return Results.NotFound();

                try
                {
                    await paypal.DeleteSavedCardAsync(method.VaultTokenId, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                await pmRepo.DeleteAsync(method, ct);

                return Results.Ok(new DeletePaymentMethodResponse { PaymentMethodId = paymentMethodId });
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<SavedPaymentMethod> service)
        => throw new System.NotSupportedException();
}

public class DeletePaymentMethodRequest : BaseRequest { }

public class DeletePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
}

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<PaymentMethod>>
{
    private readonly IPayPalService _payPal;

    public DeletePaymentMethodEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId,
                   IRepository<PaymentMethod> methodRepository,
                   HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                var spec = new PaymentMethodByIdAndBuyerIdSpec(paymentMethodId, buyer);
                var method = await methodRepository.FirstOrDefaultAsync(spec);
                if (method == null)
                    return Results.NotFound(new { error = "Payment method not found or does not belong to you." });

                try
                {
                    await _payPal.DeleteVaultedCardAsync(method.PayPalTokenId, ctx.RequestAborted);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                await methodRepository.DeleteAsync(method);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<PaymentMethod> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}

using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    private readonly PayPalService _payPal;

    public DeletePaymentMethodEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId,
                   IRepository<SavedPaymentMethod> repository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId);
                var method = await repository.FirstOrDefaultAsync(spec, ct);

                if (method == null) return Results.NotFound();

                await _payPal.DeleteCardAsync(method.VaultTokenId, ct);

                method.MarkDeleted();
                await repository.UpdateAsync(method, ct);

                return Results.NoContent();
            })
            .Produces(204)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<SavedPaymentMethod> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class DeletePaymentMethodRequest : BaseRequest { }

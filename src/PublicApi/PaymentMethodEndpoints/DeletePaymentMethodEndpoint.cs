using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    private readonly PayPalClient _payPalClient;

    public DeletePaymentMethodEndpoint(PayPalClient payPalClient)
    {
        _payPalClient = payPalClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IRepository<SavedPaymentMethod> repository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = buyerId },
                    repository);
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<SavedPaymentMethod> repository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new SavedPaymentMethodsByBuyerSpec(request.BuyerId);
        var methods = await repository.ListAsync(spec);
        var method = methods.FirstOrDefault(m => m.Id == request.PaymentMethodId);

        if (method == null)
            return Results.NotFound(new { error = "Payment method not found." });

        try
        {
            await _payPalClient.DeleteVaultPaymentTokenAsync(method.PayPalVaultTokenId);
        }
        catch (PayPalException ex) when (ex.StatusCode == 404)
        {
            // Token not found in vault — treat as already deleted, proceed
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }

        method.SoftDelete();
        await repository.UpdateAsync(method);

        return Results.NoContent();
    }
}

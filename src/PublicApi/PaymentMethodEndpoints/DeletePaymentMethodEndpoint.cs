using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, IRepository<SavedPaymentMethod>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext, IRepository<SavedPaymentMethod> methodRepo, IPaymentService paymentService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(methodRepo, paymentService, paymentMethodId, userId);
            })
            .Produces(204)
            .WithName("DeletePaymentMethod")
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<SavedPaymentMethod> methodRepo, IPaymentService paymentService,
        int paymentMethodId, string userId)
    {
        var method = await methodRepo.GetByIdAsync(paymentMethodId);
        if (method == null)
            return Results.NotFound();

        if (method.BuyerId != userId)
            return Results.Forbid();

        try
        {
            await paymentService.DeleteSavedCardAsync(method.PayPalPaymentTokenId);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Failed to delete card from PayPal: {ex.Message}" });
        }

        await methodRepo.DeleteAsync(method);
        await methodRepo.SaveChangesAsync();

        return Results.NoContent();
    }
}

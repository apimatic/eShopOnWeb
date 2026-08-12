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

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<SavedPaymentMethod>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext httpContext, IRepository<SavedPaymentMethod> methodRepo, IPaymentService paymentService) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(request, methodRepo, paymentService, userId);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithName("SavePaymentMethod")
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> methodRepo,
        IPaymentService paymentService, string userId)
    {
        try
        {
            var cardDetails = new SavedCardDetails();
            try
            {
                cardDetails = await paymentService.SaveCardAsync(userId, request.CardToken, request.CardholderName);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Failed to save card: {ex.Message}" });
            }

            var method = new SavedPaymentMethod(
                buyerId: userId,
                payPalPaymentTokenId: cardDetails.Id,
                cardLastFourDigits: cardDetails.LastFourDigits,
                cardBrand: cardDetails.Brand,
                cardholderName: cardDetails.CardholderName,
                cardExpiryDate: cardDetails.ExpiryDate
            );

            await methodRepo.AddAsync(method);
            await methodRepo.SaveChangesAsync();

            return Results.Ok(new SavePaymentMethodResponse { PaymentMethodId = method.Id.ToString() });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public record SavePaymentMethodRequest(string CardToken, string? CardholderName);
public record SavePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
}

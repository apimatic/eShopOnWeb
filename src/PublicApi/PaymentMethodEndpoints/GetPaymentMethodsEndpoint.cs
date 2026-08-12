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

public class GetPaymentMethodsEndpoint : IEndpoint<IResult, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IRepository<SavedPaymentMethod> methodRepo) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                return await HandleAsync(methodRepo, userId);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithName("GetPaymentMethods")
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<SavedPaymentMethod> methodRepo, string userId)
    {
        var methods = await methodRepo.ListAsync(m => m.BuyerId == userId);
        var result = methods.Select(m => new PaymentMethodDto
        {
            Id = m.Id.ToString(),
            CardLastFourDigits = m.CardLastFourDigits,
            CardBrand = m.CardBrand,
            CardholderName = m.CardholderName,
            CardExpiryDate = m.CardExpiryDate,
            CreatedAt = m.CreatedAt
        }).ToList();

        return Results.Ok(result);
    }
}

public record PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string? CardLastFourDigits { get; set; }
    public string? CardBrand { get; set; }
    public string? CardholderName { get; set; }
    public string? CardExpiryDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

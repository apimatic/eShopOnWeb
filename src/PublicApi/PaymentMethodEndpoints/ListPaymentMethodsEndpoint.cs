using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, IRepository<SavedPaymentMethod> methodRepo) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

                var methods = await methodRepo.ListAsync(
                    new SavedPaymentMethodsByBuyerSpec(username));

                var dtos = methods.Select(m => new PaymentMethodDto
                {
                    PaymentMethodId = m.Id,
                    Last4 = m.Last4,
                    Brand = m.Brand,
                    ExpiryYear = m.ExpiryYear,
                    ExpiryMonth = m.ExpiryMonth
                });

                return Results.Ok(new { PaymentMethods = dtos });
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<SavedPaymentMethod> repository)
        => throw new NotImplementedException();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Last4 { get; set; } = "";
    public string Brand { get; set; } = "";
    public int ExpiryYear { get; set; }
    public int ExpiryMonth { get; set; }
}

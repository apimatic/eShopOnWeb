using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record PaymentMethodDto(string Id, string? Last4, string? Brand, string? Expiry, string? CardType);

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPayPalPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPayPalPaymentService payPal) =>
            {
                return await HandleAsync(payPal);
            })
            .Produces<List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPayPalPaymentService payPal)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var user = httpCtx?.User;
        var userId = user?.FindFirstValue(ClaimTypes.Email)
                  ?? user?.FindFirstValue("sub")
                  ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        try
        {
            var tokens = await payPal.ListVaultedTokensAsync(userId, ct);
            var dtos = tokens.Select(t => new PaymentMethodDto(t.TokenId, t.Last4, t.Brand, t.Expiry, t.CardType)).ToList();
            return Results.Ok(dtos);
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}

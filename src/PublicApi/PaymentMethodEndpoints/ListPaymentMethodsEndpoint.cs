using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the caller's saved cards (safe display metadata only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService savedPaymentMethodService) =>
            {
                return await HandleAsync(user, savedPaymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISavedPaymentMethodService savedPaymentMethodService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await savedPaymentMethodService.ListAsync(buyerId);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.Id,
                CardBrand = m.CardBrand,
                LastDigits = m.LastDigits,
                Expiry = m.Expiry,
                CardholderName = m.CardholderName,
                CreatedAt = m.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

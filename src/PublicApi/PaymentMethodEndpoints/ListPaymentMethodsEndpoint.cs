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

/// <summary>
/// Lists the signed-in shopper's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;

    public ListPaymentMethodsEndpoint(ISavedPaymentMethodService savedPaymentMethodService)
    {
        _savedPaymentMethodService = savedPaymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await _savedPaymentMethodService.ListAsync(buyerId);
        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => new SavedPaymentMethodDto
            {
                PaymentMethodId = m.Id,
                Brand = m.Brand,
                LastFourDigits = m.LastFourDigits,
                Expiry = m.Expiry,
                CardholderName = m.CardholderName,
                CreatedAt = m.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new List<SavedPaymentMethodDto>();
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the caller's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, IPaymentMethodService paymentMethodService) => await HandleAsync(context, paymentMethodService))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, IPaymentMethodService service)
    {
        var saved = await service.ListAsync(context.User.BuyerId());

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = saved.Select(pm => new SavedCardDto
            {
                PaymentMethodId = pm.Id,
                Brand = pm.Brand,
                LastFourDigits = pm.LastFourDigits,
                Expiry = pm.Expiry,
                CardholderName = pm.CardholderName,
                CreatedAt = pm.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

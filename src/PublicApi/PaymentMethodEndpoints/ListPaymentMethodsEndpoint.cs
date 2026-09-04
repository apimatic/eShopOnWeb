using System;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
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
/// The caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IPaymentProcessingService paymentProcessing) =>
            {
                return await HandleAsync(principal.Identity?.Name ?? string.Empty, paymentProcessing);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentProcessingService paymentProcessing)
    {
        var response = new ListPaymentMethodsResponse();

        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await paymentProcessing.GetBuyerCardsAsync(buyerId);
        response.PaymentMethods = cards.Select(c => new SavedCardDto
        {
            PaymentMethodId = c.Id,
            Brand = c.Brand,
            Last4 = c.Last4,
            Expiry = c.Expiry,
            Alias = c.Alias,
            CreatedTime = c.CreatedTime
        }).ToList();
        return Results.Ok(response);
    }
}

public class SavedCardDto
{
    public int PaymentMethodId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public string? Expiry { get; init; }
    public string? Alias { get; init; }
    public DateTimeOffset CreatedTime { get; init; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse() { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

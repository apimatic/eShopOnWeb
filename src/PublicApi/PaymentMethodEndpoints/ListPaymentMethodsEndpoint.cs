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

public class ListPaymentMethodsRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.Identity?.Name ?? string.Empty }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service)
    {
        var cards = await service.ListCardsAsync(request.BuyerId);

        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = cards.Select(c => new SavedCardDto
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                LastFourDigits = c.LastFourDigits,
                Expiry = c.Expiry,
                Description = c.Description,
                CreatedAt = c.CreatedAt.ToString("o")
            }).ToList()
        };
        return Results.Ok(response);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
    /// <summary>Populated from the JWT; never read from the request.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) {}
    public ListPaymentMethodsResponse() {}

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.Identity?.Name }, paymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var methods = await paymentMethodService.ListAsync(request.BuyerId);

        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(m => new PaymentMethodDto
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

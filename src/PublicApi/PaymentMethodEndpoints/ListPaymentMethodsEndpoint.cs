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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the caller's own saved cards, described safely.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = PaymentMapping.GetBuyerId(user) }, paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());
        var cards = await paymentService.GetSavedCardsAsync(request.BuyerId);
        response.PaymentMethods = cards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.PaymentMethodId,
            Brand = c.Brand,
            LastFourDigits = c.LastFourDigits,
            Expiry = c.Expiry,
            CardholderName = c.CardholderName,
            CreatedAt = c.CreatedAt
        }).ToList();
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

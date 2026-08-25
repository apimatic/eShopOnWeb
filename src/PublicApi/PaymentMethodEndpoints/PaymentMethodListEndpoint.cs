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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodListRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardType { get; set; }
}

public class PaymentMethodListResponse : BaseResponse
{
    public PaymentMethodListResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// The signed-in shopper's own saved cards.
/// </summary>
public class PaymentMethodListEndpoint : IEndpoint<IResult, PaymentMethodListRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISavedPaymentMethodService paymentMethodService) =>
            {
                var request = new PaymentMethodListRequest { BuyerId = httpContext.User.Identity!.Name! };
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodListRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        var response = new PaymentMethodListResponse(request.CorrelationId());

        var paymentMethods = await paymentMethodService.ListPaymentMethodsAsync(request.BuyerId);

        response.PaymentMethods = paymentMethods.Select(p => new PaymentMethodDto
        {
            PaymentMethodId = p.Id,
            Alias = p.Alias,
            Brand = p.Brand,
            Last4 = p.Last4,
            Expiry = p.Expiry,
            CardType = p.CardType
        }).ToList();

        return Results.Ok(response);
    }
}

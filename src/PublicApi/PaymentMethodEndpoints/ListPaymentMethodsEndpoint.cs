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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Returns the authenticated shopper's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IPaymentMethodService paymentMethodService) => await HandleAsync(http, paymentMethodService))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IPaymentMethodService paymentMethodService)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);

        var cards = await paymentMethodService.ListCardsAsync(buyerId, http.RequestAborted);

        var response = new ListPaymentMethodsResponse(Guid.NewGuid())
        {
            PaymentMethods = cards.Select(SavedCardDto.From).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPaymentMethodsResponse()
    {
    }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

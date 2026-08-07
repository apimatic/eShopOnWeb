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
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Returns the signed-in shopper's saved cards, newest first.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                var buyerId = user.GetBuyerId();
                var cards = await savedCardService.GetCardsForBuyerAsync(buyerId);

                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = cards.Select(PaymentMethodDto.FromEntity).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's saved cards", "Returns the caller's saved cards."));
    }

    public Task<IResult> HandleAsync(ISavedCardService savedCardService) =>
        Task.FromResult(Results.Ok());
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

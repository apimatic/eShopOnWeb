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

public class ListPaymentMethodsRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Returns the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = buyerId }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService service)
    {
        var cards = await service.ListForBuyerAsync(request.BuyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(SavedPaymentMethodDto.From).ToList()
        });
    }
}

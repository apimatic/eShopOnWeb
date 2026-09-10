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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>Returns the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service) => await HandleAsync(user, service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISavedCardService service)
    {
        var cards = await service.GetCardsAsync(user.GetBuyerId());
        return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = cards.Select(SavedCardDto.From).ToList() });
    }
}

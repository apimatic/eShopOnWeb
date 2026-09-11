using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>Lists the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync())
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var ctx = _http.HttpContext!;
        var buyerId = ctx.User.GetBuyerId();
        var savedCardService = ctx.RequestServices.GetRequiredService<ISavedCardService>();

        var cards = await savedCardService.ListCardsAsync(buyerId, ctx.RequestAborted);

        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(c => c.ToDto()).ToList()
        });
    }
}

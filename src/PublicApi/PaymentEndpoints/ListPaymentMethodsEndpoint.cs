using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsResponse
{
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService service) => await HandleAsync(service))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var cards = await service.GetSavedCardsAsync(ctx.User.BuyerId(), ctx.RequestAborted);
        return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = cards });
    }
}

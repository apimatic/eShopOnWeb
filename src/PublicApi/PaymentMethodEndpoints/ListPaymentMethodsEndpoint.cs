using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class ListPaymentMethodsResponse
{
    public List<SavedCardResponse> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.GetBuyerId() }, paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var cards = await paymentService.GetSavedCardsAsync(request.BuyerId!, CancellationToken.None);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(card => card.ToSavedCardResponse()).ToList()
        });
    }
}

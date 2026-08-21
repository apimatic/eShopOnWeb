using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Returns the signed-in shopper's saved cards (safe descriptions only).</summary>
public class GetPaymentMethodsEndpoint : IEndpoint<IResult, GetPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new GetPaymentMethodsRequest(BuyerIdentity.GetBuyerId(user)), paymentService);
            })
            .Produces<GetPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(GetPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var cards = await paymentService.GetSavedCardsAsync(request.BuyerId);
        return Results.Ok(new GetPaymentMethodsResponse { PaymentMethods = cards });
    }
}

public class GetPaymentMethodsRequest
{
    public GetPaymentMethodsRequest(string buyerId) => BuyerId = buyerId;
    public string BuyerId { get; }
}

public class GetPaymentMethodsResponse : BaseResponse
{
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}

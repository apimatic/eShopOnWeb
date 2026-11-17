using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext http, PaymentService payments) =>
                {
                    var request = new ListPaymentMethodsRequest
                    {
                        BuyerId = http.User.Identity?.Name ?? string.Empty,
                        RequestAborted = http.RequestAborted
                    };
                    return await HandleAsync(request, payments);
                })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await payments.GetSavedCardsAsync(request.BuyerId, request.RequestAborted);
        return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = cards });
    }
}

public class ListPaymentMethodsRequest : PaymentRequestBase
{
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}

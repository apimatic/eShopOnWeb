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

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (string paymentMethodId, HttpContext http, PaymentService payments) =>
                {
                    var request = new DeletePaymentMethodRequest
                    {
                        PaymentMethodId = paymentMethodId,
                        BuyerId = http.User.Identity?.Name ?? string.Empty,
                        RequestAborted = http.RequestAborted
                    };
                    return await HandleAsync(request, payments);
                })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        await payments.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId, request.RequestAborted);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : PaymentRequestBase
{
    public string PaymentMethodId { get; set; } = string.Empty;
}

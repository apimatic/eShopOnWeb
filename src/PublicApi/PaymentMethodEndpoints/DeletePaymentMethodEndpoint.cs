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

public class DeletePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public int PaymentMethodId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest
                    {
                        PaymentMethodId = paymentMethodId,
                        BuyerId = user.GetBuyerId()
                    },
                    paymentService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService)
    {
        await paymentService.DeleteSavedCardAsync(request.BuyerId!, request.PaymentMethodId, CancellationToken.None);
        return Results.NoContent();
    }
}

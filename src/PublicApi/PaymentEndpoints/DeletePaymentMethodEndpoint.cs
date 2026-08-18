using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

/// <summary>Remove one of the caller's saved cards. Afterwards it is gone and no longer usable to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var request = new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = PaymentMappers.BuyerId(user)
                };
                return await HandleAsync(request, service, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService service, CancellationToken ct)
    {
        await service.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId, ct);
        return Results.NoContent();
    }
}

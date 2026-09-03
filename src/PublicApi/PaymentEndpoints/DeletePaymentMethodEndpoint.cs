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
}

/// <summary>
/// Removes a saved card. Afterwards it no longer appears among the caller's cards and cannot be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IPaymentService service) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        await service.DeleteSavedCardAsync(ctx.User.BuyerId(), request.PaymentMethodId, ctx.RequestAborted);
        return Results.NoContent();
    }
}

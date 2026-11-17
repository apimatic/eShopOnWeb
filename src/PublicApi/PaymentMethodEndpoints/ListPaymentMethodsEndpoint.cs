using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public ListPaymentMethodsEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new ListPaymentMethodsRequest(), user, cancellationToken))
            .Produces<IReadOnlyList<SavedPaymentMethodView>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListPaymentMethodsRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        _ = request;
        var buyerId = OrderEndpoints.EndpointIdentity.GetBuyerId(user);
        var result = await OrderEndpoints.EndpointBudget.RunAsync(
            ct => _payments.ListPaymentMethodsAsync(buyerId, ct),
            cancellationToken);
        return Results.Ok(result);
    }
}

public sealed class ListPaymentMethodsRequest
{
}

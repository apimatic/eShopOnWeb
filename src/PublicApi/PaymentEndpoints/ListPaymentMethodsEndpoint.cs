using System.Collections.Generic;
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

public class ListPaymentMethodsRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public ListPaymentMethodsRequest(string buyerId) => BuyerId = buyerId;
}

public class ListPaymentMethodsResponse
{
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(user.BuyerId()), service, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service, CancellationToken ct)
    {
        var cards = await service.ListCardsAsync(request.BuyerId, ct);
        return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = cards });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
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

public class ListPaymentMethodsRequest : BaseRequest { }

public class SavedCardView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public IReadOnlyList<SavedCardView> PaymentMethods { get; set; } = new List<SavedCardView>();
}

/// <summary>Lists the signed-in shopper's own saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var cards = await service.ListForBuyerAsync(buyerId, ct);
                return Results.Ok(new ListPaymentMethodsResponse(Guid.NewGuid())
                {
                    PaymentMethods = cards.Select(c => new SavedCardView
                    {
                        PaymentMethodId = c.Id,
                        Brand = c.Brand,
                        LastFourDigits = c.LastFourDigits,
                        Expiry = c.Expiry,
                        CardholderName = c.CardholderName,
                        CreatedDate = c.CreatedDate
                    }).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service) =>
        Task.FromResult(Results.Empty as IResult);
}

using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest
{
    [JsonIgnore]
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(CatalogContext db, FinixClient finix, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _finix = finix;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int paymentMethodId) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }))
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.Id == request.PaymentMethodId && c.OwnerId == ownerId && !c.IsDeleted);

        if (savedCard is null)
        {
            return Results.NotFound();
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, CancellationToken.None);
        }
        catch (FinixApiException)
        {
            // The local record is still removed so the card can no longer be used by this application.
        }

        savedCard.Delete();
        await _db.SaveChangesAsync();

        return Results.Ok(new DeletePaymentMethodResponse(Guid.NewGuid())
        {
            PaymentMethodId = savedCard.Id,
            Deleted = true
        });
    }
}

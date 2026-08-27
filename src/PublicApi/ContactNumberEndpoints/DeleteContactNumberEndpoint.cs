using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's registered numbers. Nothing may be sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IRepository<ContactNumber> contactNumberRepository, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest { ContactNumberId = contactNumberId }, contactNumberRepository, httpContext, cancellationToken);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository)
        => throw new NotSupportedException("Use the routed overload with HttpContext.");

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var contactNumber = await contactNumberRepository.FirstOrDefaultAsync(
            new Microsoft.eShopWeb.ApplicationCore.Specifications.ContactNumberByIdSpecification(request.ContactNumberId), cancellationToken);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            // Another shopper's number is indistinguishable from a missing one.
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);

        var response = new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId,
            Deleted = true
        };
        return Results.Ok(response);
    }
}

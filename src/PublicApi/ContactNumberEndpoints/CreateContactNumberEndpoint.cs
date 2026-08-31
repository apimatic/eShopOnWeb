using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider first; what is stored is the provider's canonical form of it.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IContactNumberService contactNumberService, CancellationToken cancellationToken) =>
            {
                request.BuyerId = user.GetBuyerId();
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        if (request.BuyerId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new CreateContactNumberResponse(request.CorrelationId())
            {
                Error = "phoneNumber is required."
            });
        }

        var result = await contactNumberService.RegisterAsync(request.BuyerId, request.PhoneNumber, request.CancellationToken);

        return result.Status switch
        {
            RegisterContactNumberStatus.InvalidNumber => Results.BadRequest(new CreateContactNumberResponse(request.CorrelationId())
            {
                Error = "The provider does not consider this a usable destination number.",
                ValidationErrors = result.ValidationErrors
            }),
            RegisterContactNumberStatus.AlreadyRegistered => Results.Ok(ToResponse(request, result.ContactNumber!)),
            _ => Results.Created($"api/contact-numbers/{result.ContactNumber!.Id}", ToResponse(request, result.ContactNumber))
        };
    }

    private static CreateContactNumberResponse ToResponse(CreateContactNumberRequest request, ContactNumber contactNumber) =>
        new(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
}

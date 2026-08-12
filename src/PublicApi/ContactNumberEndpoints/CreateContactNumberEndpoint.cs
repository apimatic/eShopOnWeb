using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated and canonicalized with
/// the provider up front — an unusable destination is rejected here, not when a later message fails.
/// </summary>
public class CreateContactNumberEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IContactNumberService service) =>
                await HandleAsync(request, service))
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        var contactNumber = await service.RegisterAsync(BuyerId, request.PhoneNumber, RequestAborted);

        var response = new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

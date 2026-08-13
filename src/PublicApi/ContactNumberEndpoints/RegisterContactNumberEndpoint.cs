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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and its
/// canonical form is stored; an unusable destination is rejected here rather than at send time.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest>
{
    private readonly IOrderNotificationService _service;

    public RegisterContactNumberEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.CallerId = user.GetUserId();
                return await HandleAsync(request, ct);
            })
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, CancellationToken ct)
    {
        var response = new RegisterContactNumberResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest("A phone number is required.");

        var contactNumber = await _service.RegisterContactNumberAsync(request.CallerId, request.PhoneNumber, ct);

        response.ContactNumberId = contactNumber.Id;
        response.ContactNumber = ContactNumberDto.From(contactNumber);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

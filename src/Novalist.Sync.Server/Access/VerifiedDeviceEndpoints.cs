using System.Security.Claims;
using Novalist.Sync.Contracts;

namespace Novalist.Sync.Server.Access;

public static class VerifiedDeviceEndpoints
{
    public static IEndpointRouteBuilder MapVerifiedDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Protocol.DeviceRegistrationPath, ContextAsync).RequireAuthorization();
        app.MapPut(Protocol.VerifiedDevicesPath, RegisterAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ContextAsync(ClaimsPrincipal principal, UserDirectory users, DeviceProofServer proofs, CancellationToken ct)
    {
        if (proofs.ServerUrl is not { } serverUrl) return Results.Problem("Verified devices have not been configured.", statusCode: 503);
        try { return Results.Ok(new DeviceRegistrationContext((await users.GetOrCreateAsync(principal, ct)).Id, serverUrl)); }
        catch (AccessDeniedException error) { return Results.Problem(detail: error.Message, statusCode: 403); }
    }

    private static async Task<IResult> RegisterAsync(VerifiedDeviceRegistration request, ClaimsPrincipal principal,
        VerifiedDeviceEnrollment enrollment, DeviceProofServer proofs, CancellationToken ct)
    {
        if (proofs.ServerUrl is not { } serverUrl) return Results.Problem("Verified devices have not been configured.", statusCode: 503);
        try
        {
            await enrollment.RegisterAsync(principal, request, serverUrl, ct);
            return Results.NoContent();
        }
        catch (FormatException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (ConflictException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict); }
        catch (AccessDeniedException error) { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status403Forbidden); }
    }
}

using GoalFueled.Api.Auth;
using GoalFueled.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GoalFueled.Api.Common;

/// <summary>
/// Thin wrapper that gives every endpoint the same shape: resolve the current user, run the handler,
/// and translate <see cref="ApiException"/>s into proper HTTP problem responses.
/// </summary>
public static class ApiEndpoint
{
    /// <summary>Runs <paramref name="handler"/> with the authenticated user, or returns 401 if there is none.</summary>
    public static async Task<IActionResult> RunAuthenticated(
        HttpRequest request,
        IAuthenticator auth,
        Func<AppUser, Task<IActionResult>> handler)
    {
        var ct = request.HttpContext.RequestAborted;
        try
        {
            var user = await auth.GetCurrentUserAsync(request, ct);
            if (user is null) throw new UnauthorizedException();
            return await handler(user);
        }
        catch (ApiException ex)
        {
            return HttpResults.Problem(ex);
        }
    }
}

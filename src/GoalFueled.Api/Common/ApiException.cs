namespace GoalFueled.Api.Common;

/// <summary>Base for exceptions that map to a specific HTTP status code.</summary>
public abstract class ApiException : Exception
{
    public abstract int StatusCode { get; }
    protected ApiException(string message) : base(message) { }
}

/// <summary>401 — no valid identity on the request.</summary>
public sealed class UnauthorizedException(string message = "Authentication required.") : ApiException(message)
{
    public override int StatusCode => 401;
}

/// <summary>403 — authenticated but not allowed to perform the action.</summary>
public sealed class ForbiddenException(string message = "You do not have access to this resource.") : ApiException(message)
{
    public override int StatusCode => 403;
}

/// <summary>404 — resource not found (or not visible to the caller).</summary>
public sealed class NotFoundException(string message = "Resource not found.") : ApiException(message)
{
    public override int StatusCode => 404;
}

/// <summary>400 — the request payload failed validation.</summary>
public sealed class ValidationException(string message) : ApiException(message)
{
    public override int StatusCode => 400;
}

namespace Web.Configuration;

/// <summary>
/// Picks the API rate limit bucket: the JWT subject for authenticated callers, otherwise the remote IP.
/// </summary>
public static class ApiRateLimitPartition
{
    public static string KeyFor(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (context.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(subject))
        {
            return $"sub:{subject}";
        }

        return $"ip:{context.Connection.RemoteIpAddress}";
    }
}

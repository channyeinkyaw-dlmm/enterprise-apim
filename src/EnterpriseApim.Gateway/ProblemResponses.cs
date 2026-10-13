using System.Text.Json;

namespace EnterpriseApim.Gateway;

internal static class ProblemResponses
{
    public static Task WriteAsync(HttpResponse response, int status, string title, string detail,
        CancellationToken cancellationToken = default)
    {
        response.StatusCode = status;
        var body = new ProblemBody(title, status, detail, response.HttpContext.TraceIdentifier);
        return response.WriteAsJsonAsync(body, options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
    }

    private sealed record ProblemBody(string Title, int Status, string Detail, string TraceId);
}

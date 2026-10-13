using System.Text.Json;
using System.Text.RegularExpressions;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Routing.Patterns;

namespace EnterpriseApim.AdminApi;

/// <summary>Collects field errors and turns them into a 400 validation problem.</summary>
internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool IsValid => _errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
    }

    public IEnumerable<(string Field, string Message)> All =>
        _errors.SelectMany(e => e.Value.Select(message => (e.Key, message)));

    public IResult ToResult() =>
        TypedResults.ValidationProblem(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}

internal static partial class Rules
{
    public static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static readonly string[] LoadBalancingPolicies =
        ["RoundRobin", "PowerOfTwoChoices", "LeastRequests", "Random", "FirstAlphabetical"];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,99}$")]
    public static partial Regex Identifier();

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$")]
    private static partial Regex ScopePattern();

    public static bool IsValidScope(string scope) => scope == ClientClaims.AllScopes || ScopePattern().IsMatch(scope);

    public static string[] NormalizeList(string[]? values) =>
        (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    public static void ValidateRoutePath(string? path, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            errors.Add("path", "Enter a path that starts with /, for example /api/orders/{**catch-all}.");
            return;
        }

        try
        {
            RoutePatternFactory.Parse(path);
        }
        catch (RoutePatternException ex)
        {
            errors.Add("path", $"The path pattern is not valid: {ex.Message}");
        }
    }

    public static void ValidateTransforms(string? json, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("transformsJson", "Transforms must be a JSON array, for example [{\"PathRemovePrefix\": \"/api\"}].");
                return;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.EnumerateObject().Any() ||
                    item.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String))
                {
                    errors.Add("transformsJson", "Each transform must be an object whose values are all strings.");
                    return;
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add("transformsJson", $"Transforms are not valid JSON: {ex.Message}");
        }
    }
}

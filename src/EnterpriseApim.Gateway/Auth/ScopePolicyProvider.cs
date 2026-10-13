using System.Collections.Concurrent;
using EnterpriseApim.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EnterpriseApim.Gateway.Auth;

/// <summary>
/// Resolves policy names like "scope:orders.read" on the fly, so routes stored in the
/// database can require any scope without registering policies in code.
/// </summary>
public sealed class ScopePolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "scope:";

    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public static string PolicyNameFor(string scope) => Prefix + scope;

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return base.GetPolicyAsync(policyName);

        var scope = policyName[Prefix.Length..];
        if (scope.Length == 0)
            return Task.FromResult<AuthorizationPolicy?>(null);

        var policy = _policies.GetOrAdd(scope, required => new AuthorizationPolicyBuilder(ApiKeyDefaults.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ClientClaims.TenantId)
            .RequireAssertion(context =>
                context.User.HasClaim(ClientClaims.Scope, required) ||
                context.User.HasClaim(ClientClaims.Scope, ClientClaims.AllScopes))
            .Build());

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

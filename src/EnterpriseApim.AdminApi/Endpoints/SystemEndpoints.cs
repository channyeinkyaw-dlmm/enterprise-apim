namespace EnterpriseApim.AdminApi.Endpoints;

public static class SystemEndpoints
{
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        // Forces every gateway instance to reload routes and drop cached keys.
        api.MapPost("/gateway/reload", async (ChangeNotifier notifier, CancellationToken ct) =>
        {
            await notifier.ResyncAsync(ct);
            return TypedResults.Accepted((string?)null);
        }).WithTags("System");

        return api;
    }
}

# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY samples/EnterpriseApim.SampleBackend/ samples/EnterpriseApim.SampleBackend/
RUN dotnet publish samples/EnterpriseApim.SampleBackend/EnterpriseApim.SampleBackend.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EnterpriseApim.SampleBackend.dll"]

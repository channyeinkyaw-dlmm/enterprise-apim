# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/EnterpriseApim.Core/EnterpriseApim.Core.csproj src/EnterpriseApim.Core/
COPY src/EnterpriseApim.Gateway/EnterpriseApim.Gateway.csproj src/EnterpriseApim.Gateway/
RUN dotnet restore src/EnterpriseApim.Gateway/EnterpriseApim.Gateway.csproj
COPY src/EnterpriseApim.Core/ src/EnterpriseApim.Core/
COPY src/EnterpriseApim.Gateway/ src/EnterpriseApim.Gateway/
RUN dotnet publish src/EnterpriseApim.Gateway/EnterpriseApim.Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EnterpriseApim.Gateway.dll"]

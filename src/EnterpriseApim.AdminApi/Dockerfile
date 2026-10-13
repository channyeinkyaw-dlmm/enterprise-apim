# Build context: repository root (see docker-compose.yml)

# 1. Admin console (React)
FROM node:22-alpine AS console
WORKDIR /console
COPY src/EnterpriseApim.AdminConsole/package.json src/EnterpriseApim.AdminConsole/package-lock.json ./
RUN npm ci
COPY src/EnterpriseApim.AdminConsole/ ./
RUN npm run build

# 2. Admin API (.NET)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/EnterpriseApim.Core/EnterpriseApim.Core.csproj src/EnterpriseApim.Core/
COPY src/EnterpriseApim.AdminApi/EnterpriseApim.AdminApi.csproj src/EnterpriseApim.AdminApi/
RUN dotnet restore src/EnterpriseApim.AdminApi/EnterpriseApim.AdminApi.csproj
COPY src/EnterpriseApim.Core/ src/EnterpriseApim.Core/
COPY src/EnterpriseApim.AdminApi/ src/EnterpriseApim.AdminApi/
RUN dotnet publish src/EnterpriseApim.AdminApi/EnterpriseApim.AdminApi.csproj -c Release -o /app/publish --no-restore

# 3. Runtime: API serves the console from wwwroot
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=console /console/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EnterpriseApim.AdminApi.dll"]

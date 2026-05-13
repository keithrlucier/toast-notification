# Build stage — restore and publish the API project only.
# The Agent project (Windows-only) is intentionally excluded.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/ToastRevival.Api/ToastRevival.Api.csproj src/ToastRevival.Api/
RUN dotnet restore src/ToastRevival.Api/ToastRevival.Api.csproj

COPY src/ToastRevival.Api/ src/ToastRevival.Api/
RUN dotnet publish src/ToastRevival.Api/ToastRevival.Api.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Persist uploaded assets (tenant logos, hero images) across container restarts.
# Mount a named volume at /app/wwwroot/assets in docker-compose.yml.
VOLUME ["/app/wwwroot/assets"]

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ToastRevival.Api.dll"]

# ─── Build stage ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer-cached restore
COPY "src/CartService.Domain/CartService.Domain.csproj" src/CartService.Domain/
COPY "src/CartService.Infrastructure/CartService.Infrastructure.csproj" src/CartService.Infrastructure/
COPY "src/CartService.Api/CartService.Api.csproj" src/CartService.Api/

RUN dotnet restore src/CartService.Api/CartService.Api.csproj

# Copy source code
COPY "src/" src/

RUN dotnet publish src/CartService.Api/CartService.Api.csproj -c Release -o /app/publish --no-restore

# ─── Runtime stage ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Run as the non-root user built into the .NET images (UID $APP_UID)
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CartService.Api.dll"]

# ===========================================================================
# usebens-motor-decisao - container image
# Multi-stage build: compile with the .NET SDK, run on the slim ASP.NET runtime.
# ===========================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (leverages Docker layer cache when only source changes).
COPY MotorDecisao.slnx ./
COPY src/MotorDecisao.Domain/MotorDecisao.Domain.csproj src/MotorDecisao.Domain/
COPY src/MotorDecisao.Application/MotorDecisao.Application.csproj src/MotorDecisao.Application/
COPY src/MotorDecisao.Infrastructure/MotorDecisao.Infrastructure.csproj src/MotorDecisao.Infrastructure/
COPY src/MotorDecisao.Api/MotorDecisao.Api.csproj src/MotorDecisao.Api/
RUN dotnet restore src/MotorDecisao.Api/MotorDecisao.Api.csproj

# Build and publish the API.
COPY . .
RUN dotnet publish src/MotorDecisao.Api/MotorDecisao.Api.csproj \
    -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the container HEALTHCHECK.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Run as a non-root user (present in the base image).
USER $APP_UID

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "MotorDecisao.Api.dll"]

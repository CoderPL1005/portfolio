FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY src/Portfolio.Domain/Portfolio.Domain.csproj src/Portfolio.Domain/
COPY src/Portfolio.Application/Portfolio.Application.csproj src/Portfolio.Application/
COPY src/Portfolio.Infrastructure/Portfolio.Infrastructure.csproj src/Portfolio.Infrastructure/
COPY src/Portfolio.Api/Portfolio.Api.csproj src/Portfolio.Api/
RUN dotnet restore src/Portfolio.Api/Portfolio.Api.csproj

COPY src/ src/
RUN dotnet publish src/Portfolio.Api/Portfolio.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .

USER app
ENTRYPOINT ["dotnet", "Portfolio.Api.dll"]

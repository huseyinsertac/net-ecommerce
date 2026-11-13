# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore src/Bootstrapper/Shop.Api/Shop.Api.csproj
RUN dotnet publish src/Bootstrapper/Shop.Api/Shop.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ARG APP_VERSION=dev
ENV APP_VERSION=${APP_VERSION} \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shop.Api.dll"]

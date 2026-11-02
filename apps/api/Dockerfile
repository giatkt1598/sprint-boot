# API image: the Host and the DbMigrator, published side by side so a release always migrates with its own code.
# Build from the repository root: docker build -f apps/api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY apps/api/Directory.Build.props apps/api/Directory.Packages.props apps/api/
COPY apps/api/src apps/api/src
RUN dotnet publish apps/api/src/SprintBoot.Host -c Release -o /out/host \
    && dotnet publish apps/api/src/SprintBoot.DbMigrator -c Release -o /out/migrator

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app/host
COPY --from=build /out/host /app/host
COPY --from=build /out/migrator /app/migrator
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
EXPOSE 8080
# The migrator runs with: dotnet /app/migrator/SprintBoot.DbMigrator.dll
ENTRYPOINT ["dotnet", "/app/host/SprintBoot.Host.dll"]

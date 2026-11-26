# Build once, promote the same image by digest to every environment (ES-049). Base images are pinned by digest (ES-013).
# Build from the repository root:  docker build -f src/UmbracoSample.Web/Dockerfile .

# The SDK tag matches global.json exactly (rollForward disable); the digest pins it.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 CI=true

# Restore first, from committed lock files only, so the layer caches until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/UmbracoSample.ServiceDefaults/*.csproj src/UmbracoSample.ServiceDefaults/packages.lock.json src/UmbracoSample.ServiceDefaults/
COPY src/UmbracoSample.Domain/*.csproj src/UmbracoSample.Domain/packages.lock.json src/UmbracoSample.Domain/
COPY src/UmbracoSample.Components/*.csproj src/UmbracoSample.Components/packages.lock.json src/UmbracoSample.Components/
COPY src/UmbracoSample.Governance/*.csproj src/UmbracoSample.Governance/packages.lock.json src/UmbracoSample.Governance/
COPY src/UmbracoSample.Security/*.csproj src/UmbracoSample.Security/packages.lock.json src/UmbracoSample.Security/
COPY src/UmbracoSample.Web/*.csproj src/UmbracoSample.Web/packages.lock.json src/UmbracoSample.Web/
COPY infra/Directory.Packages.props infra/
COPY infra/UmbracoSample.Infra.DatabaseUser/*.csproj infra/UmbracoSample.Infra.DatabaseUser/packages.lock.json infra/UmbracoSample.Infra.DatabaseUser/
RUN dotnet restore src/UmbracoSample.Web/UmbracoSample.Web.csproj \
    && dotnet restore infra/UmbracoSample.Infra.DatabaseUser/UmbracoSample.Infra.DatabaseUser.csproj

COPY src/UmbracoSample.ServiceDefaults/ src/UmbracoSample.ServiceDefaults/
COPY src/UmbracoSample.Domain/ src/UmbracoSample.Domain/
COPY src/UmbracoSample.Components/ src/UmbracoSample.Components/
COPY src/UmbracoSample.Governance/ src/UmbracoSample.Governance/
COPY src/UmbracoSample.Security/ src/UmbracoSample.Security/
COPY src/UmbracoSample.Web/ src/UmbracoSample.Web/
# The stylesheet is built before the image (make frontend, or the CI step), so this image needs no Node toolchain.
RUN test -f src/UmbracoSample.Web/wwwroot/dist/site.css || { echo 'Run make frontend before building the image.' >&2; exit 1; }
RUN dotnet publish src/UmbracoSample.Web/UmbracoSample.Web.csproj -c Release --no-restore -o /app /p:UseAppHost=false
# The database user tool ships in the same image as a second entry point, run by a Container Apps job inside the
# virtual network (ADR 0017), so no second image or registry is needed.
COPY infra/UmbracoSample.Infra.DatabaseUser/ infra/UmbracoSample.Infra.DatabaseUser/
RUN dotnet publish infra/UmbracoSample.Infra.DatabaseUser/UmbracoSample.Infra.DatabaseUser.csproj -c Release --no-restore -o /app/tools/database-user /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS final
WORKDIR /app
COPY --from=build /app .

# Umbraco writes indexes, temp files and logs under umbraco/; media moves to Blob storage in Azure.
# Only these paths are writable by the non-root app user.
RUN mkdir -p umbraco/Data umbraco/Logs wwwroot/media \
    && chown -R $APP_UID:$APP_UID umbraco wwwroot/media

ARG SOURCE_REVISION=unknown
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    OTEL_RESOURCE_ATTRIBUTES=service.version=${SOURCE_REVISION}
LABEL org.opencontainers.image.source="https://github.com/JDetmar/umbraco-sample" \
      org.opencontainers.image.revision="${SOURCE_REVISION}"

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "UmbracoSample.Web.dll"]

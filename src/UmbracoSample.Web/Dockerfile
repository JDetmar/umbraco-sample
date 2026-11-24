# syntax=docker/dockerfile:1
# Build once, promote the same image by digest to every environment (ES-049). Base images are pinned by digest (ES-013).
# Build from the repository root:  docker build -f src/UmbracoSample.Web/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 CI=true

# Restore first, from committed lock files only, so the layer caches until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/UmbracoSample.ServiceDefaults/*.csproj src/UmbracoSample.ServiceDefaults/packages.lock.json src/UmbracoSample.ServiceDefaults/
COPY src/UmbracoSample.Web/*.csproj src/UmbracoSample.Web/packages.lock.json src/UmbracoSample.Web/
RUN dotnet restore src/UmbracoSample.Web/UmbracoSample.Web.csproj

COPY src/UmbracoSample.ServiceDefaults/ src/UmbracoSample.ServiceDefaults/
COPY src/UmbracoSample.Web/ src/UmbracoSample.Web/
RUN dotnet publish src/UmbracoSample.Web/UmbracoSample.Web.csproj -c Release --no-restore -o /app /p:UseAppHost=false

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

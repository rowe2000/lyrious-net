# Lyrious on one address: Lyrious.Api, which also serves the web app (Lyrious.Webapp, WebAssembly)
# and the product website (Lyrious.Website).
# Used by Railway, which builds this file from the repository root and sets PORT at run time.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# wasm-tools compiles the web app to WebAssembly (AOT, with Avalonia's native libraries);
# its emscripten toolchain needs python3.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/* \
    && dotnet workload install wasm-tools

COPY . .
# The version, branch and commit for the About page (see Directory.Build.props): from git when the
# build has the .git folder, else from these. Railway sets the RAILWAY_GIT_* ones; set
# LYRIOUS_VERSION (e.g. 0.1.0.7) as a variable on the service for the version number.
ARG LYRIOUS_VERSION
ARG LYRIOUS_BRANCH
ARG LYRIOUS_COMMIT
ARG RAILWAY_GIT_BRANCH
ARG RAILWAY_GIT_COMMIT_SHA
RUN dotnet publish Lyrious.Api/Lyrious.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# SQLite (the default off Windows), in /data: mount a volume there so the database outlives deploys.
ENV Database__Provider=Sqlite \
    Database__SqliteFile=/data/lyrious-api.db
# The songs' sound files on the volume too, so they survive deploys (in the container they'd be lost
# with each one, and the band's other devices couldn't fetch them). The same as deploy/Dockerfile's.
ENV Media__Folder=/data/media
# The platform's proxy says how the app was reached (https), e.g. for links in emails.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
RUN mkdir -p /data

ENTRYPOINT ["dotnet", "Lyrious.Api.dll"]

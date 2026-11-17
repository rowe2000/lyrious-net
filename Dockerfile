# Lyrious on one address: Lyrious.Api, which also serves the web app (Lyrious.Web, WebAssembly).
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
RUN dotnet publish Lyrious.Api/Lyrious.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# SQLite (the default off Windows), in /data: mount a volume there so the database outlives deploys.
ENV Database__Provider=Sqlite \
    Database__SqliteFile=/data/lyrious-api.db
# The platform's proxy says how the app was reached (https), e.g. for links in emails.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
RUN mkdir -p /data

ENTRYPOINT ["dotnet", "Lyrious.Api.dll"]

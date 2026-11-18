# Runs the published server (API + web app). Built by deploy/railway.sh, which publishes
# Lyrious.Api on this machine first (the web app's AOT compile needs the .NET wasm tools) and
# sends only the result, so the host has nothing to compile.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY app/ .
# The database lives on the volume mounted at /data, so it survives deploys.
ENV Database__Provider=Sqlite \
    Database__SqliteFile=/data/lyrious.db
ENTRYPOINT ["dotnet", "Lyrious.Api.dll"]

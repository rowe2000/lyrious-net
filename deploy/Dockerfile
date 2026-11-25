# Runs the published server (API + web app). Built by deploy/railway.sh, which publishes
# Lyrious.Api on this machine first (the web app's AOT compile needs the .NET wasm tools, and the
# version comes from this machine's git tags) and sends only the result, so the host has nothing to
# compile. Keep it in step with the Dockerfile at the root (Railway's builds from GitHub use that).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY app/ .
# The database lives on the volume mounted at /data, so it survives deploys. The same file as the
# root Dockerfile's, so either way of deploying uses the same data.
ENV Database__Provider=Sqlite \
    Database__SqliteFile=/data/lyrious-api.db
# The songs' sound files on the volume too, so they survive deploys (in the container they'd be lost
# with each one, and the band's other devices couldn't fetch them).
ENV Media__Folder=/data/media
# The platform's proxy says how the app was reached (https), e.g. for links in emails.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
RUN mkdir -p /data
ENTRYPOINT ["dotnet", "Lyrious.Api.dll"]

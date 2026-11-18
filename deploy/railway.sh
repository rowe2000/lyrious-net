#!/bin/sh
# Publishes the server with the web app (Release, AOT) and sends it to Railway.
# Before the first run: `railway login`, then in this folder `railway init` (or `railway link`),
# add a volume mounted at /data, and set the variable Jwt__Key to a long random secret.
set -e
cd "$(dirname "$0")"
rm -rf app
dotnet publish ../Lyrious.Api -c Release -o app
# Local files that mustn't go along: this machine's database.
rm -f app/*.db
railway up

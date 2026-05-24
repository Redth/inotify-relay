# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY InotifyRelay.sln ./
COPY src/ ./src/

RUN dotnet restore src/InotifyRelay.Web/InotifyRelay.Web.csproj
RUN dotnet publish src/InotifyRelay.Web/InotifyRelay.Web.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# tini for clean signal forwarding (PID 1)
RUN apt-get update && apt-get install -y --no-install-recommends tini \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/ ./

ENV ASPNETCORE_URLS=http://+:8080 \
    INOTIFY_RELAY_DATA=/data \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "InotifyRelay.Web.dll"]

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

ARG ASSETSTUDIO_REPOSITORY=https://github.com/Team-Haruki/AssetStudio.git
ARG ASSETSTUDIO_BRANCH=sekai-modified

ENV DEBIAN_FRONTEND=noninteractive \
    ASSETSTUDIO_REPOSITORY=${ASSETSTUDIO_REPOSITORY} \
    ASSETSTUDIO_BRANCH=${ASSETSTUDIO_BRANCH} \
    ASSETSTUDIO_ROOT=/src/AssetStudio

WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    git \
    clang \
    zlib1g-dev \
    binutils && \
    rm -rf /var/lib/apt/lists/*

COPY scripts/prepare-assetstudio.sh scripts/prepare-assetstudio.sh
RUN scripts/prepare-assetstudio.sh

COPY . Haruki-3D-Exporter
WORKDIR /src/Haruki-3D-Exporter

RUN dotnet restore \
    -p:AssetStudioRoot="${ASSETSTUDIO_ROOT}" \
    -p:RestoreConfigFile=NuGet.Config
RUN dotnet publish -c Release -o /app/exporter \
    --no-restore \
    -p:AssetStudioRoot="${ASSETSTUDIO_ROOT}"

FROM mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
WORKDIR /app
COPY --from=build /app/exporter /app/exporter

ENTRYPOINT ["dotnet", "/app/exporter/Haruki-3D-Exporter.dll"]

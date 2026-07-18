FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

ARG ASSETSTUDIO_REPOSITORY=https://github.com/Team-Haruki/AssetStudio.git
ARG ASSETSTUDIO_BRANCH=sekai-modified
ARG ASSETSTUDIO_REVISION=ae75b2e548137598505afcba1340707c4a794add

ENV DEBIAN_FRONTEND=noninteractive \
    ASSETSTUDIO_REPOSITORY=${ASSETSTUDIO_REPOSITORY} \
    ASSETSTUDIO_BRANCH=${ASSETSTUDIO_BRANCH} \
    ASSETSTUDIO_REVISION=${ASSETSTUDIO_REVISION} \
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
RUN bash scripts/prepare-assetstudio.sh

COPY . Haruki-3D-Exporter
WORKDIR /src/Haruki-3D-Exporter

RUN dotnet restore \
    -r linux-x64 \
    -p:AssetStudioRoot="${ASSETSTUDIO_ROOT}" \
    -p:RestoreConfigFile=NuGet.Config
RUN dotnet publish -c Release -r linux-x64 -o /app/exporter \
    --self-contained true \
    --no-restore \
    -p:AssetStudioRoot="${ASSETSTUDIO_ROOT}"

FROM rust:1-bookworm AS oxipng
RUN cargo install oxipng --version 9.1.5 --locked

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-bookworm-slim

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    libxml2 && \
    rm -rf /var/lib/apt/lists/*
COPY --from=build /app/exporter /app/exporter
COPY --from=oxipng /usr/local/cargo/bin/oxipng /usr/local/bin/oxipng

ENTRYPOINT ["/app/exporter/Haruki-3D-Exporter"]

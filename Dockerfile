FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build

ARG ASSETSTUDIO_REPOSITORY=https://github.com/Team-Haruki/AssetStudio.git
ARG ASSETSTUDIO_BRANCH=sekai-modified
ARG ASSETSTUDIO_REVISION=70e6ec3e00665ff3c36a8af08f8e2824c2eba873

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

ARG KTX_VERSION=4.4.2
ARG KTX_DEB_SHA256=ca635ed489d8bf54fac8d7687056c651193de0740830a7738cc034adc63e3027
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    libxml2 && \
    curl -fsSL \
      "https://github.com/KhronosGroup/KTX-Software/releases/download/v${KTX_VERSION}/KTX-Software-${KTX_VERSION}-Linux-x86_64.deb" \
      -o /tmp/ktx.deb && \
    echo "${KTX_DEB_SHA256}  /tmp/ktx.deb" | sha256sum -c - && \
    apt-get install -y --no-install-recommends /tmp/ktx.deb && \
    rm -f /tmp/ktx.deb && \
    rm -rf /var/lib/apt/lists/*
COPY --from=build /app/exporter /app/exporter
COPY --from=oxipng /usr/local/cargo/bin/oxipng /usr/local/bin/oxipng

ENTRYPOINT ["/app/exporter/Haruki-3D-Exporter"]

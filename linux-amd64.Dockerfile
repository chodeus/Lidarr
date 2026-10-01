# syntax=docker/dockerfile:1
# check=skip=InvalidDefaultArgInFrom
ARG UPSTREAM_IMAGE
ARG UPSTREAM_TAG_SHA

FROM ${UPSTREAM_IMAGE}:${UPSTREAM_TAG_SHA}
EXPOSE 8686
ARG IMAGE_STATS
ENV IMAGE_STATS=${IMAGE_STATS} WEBUI_PORTS="8686/tcp"

RUN apk add --no-cache libintl sqlite-libs icu-libs chromaprint

ARG VERSION
ARG VERSION_BRANCH
ARG PACKAGE_VERSION=${VERSION}

# lidarr.tar.gz comes from ./build.sh compile.
RUN --mount=type=bind,source=lidarr.tar.gz,target=/tmp/lidarr.tar.gz \
    mkdir "${APP_DIR}/bin" && \
    tar xzf /tmp/lidarr.tar.gz -C "${APP_DIR}/bin" --strip-components=1 && \
    rm -rf "${APP_DIR}/bin/Lidarr.Update" && \
    rm -f "${APP_DIR}/bin/fpcalc" && \
    echo -e "PackageVersion=${PACKAGE_VERSION}\nPackageAuthor=[chodeus](https://github.com/chodeus)\nUpdateMethod=Docker\nBranch=${VERSION_BRANCH}" > "${APP_DIR}/package_info" && \
    chmod -R u=rwX,go=rX "${APP_DIR}"

ARG SLEEZER_REPO
ARG SLEEZER_VERSION
ARG SLEEZER_ZIP_SHA256

# init-setup-app copies ${APP_DIR}/plugins into the config volume, where Lidarr loads plugins from.
RUN curl -fsSL -o /tmp/sleezer.zip "https://github.com/${SLEEZER_REPO}/releases/download/${SLEEZER_VERSION}/Sleezer-${SLEEZER_VERSION}.net8.0.zip" && \
    echo "${SLEEZER_ZIP_SHA256}  /tmp/sleezer.zip" | sha256sum -c - && \
    mkdir -p "${APP_DIR}/plugins/${SLEEZER_REPO}" && \
    unzip -q /tmp/sleezer.zip -d "${APP_DIR}/plugins/${SLEEZER_REPO}" && \
    rm /tmp/sleezer.zip && \
    chmod -R u=rwX,go=rX "${APP_DIR}/plugins"

COPY root/ /
RUN find /etc/s6-overlay/s6-rc.d -name "run*" -execdir chmod +x {} +

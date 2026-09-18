#!/bin/bash

if [[ -z ${1:-} ]]; then
    echo "Usage: ./build.sh update"
    echo "       ./build.sh source"
    echo "       ./build.sh migrations"
    echo "       ./build.sh compile"
    echo "       ./build.sh amd64"
    echo "       ./build.sh packages <image>"
    exit 1
fi

set -euo pipefail

if [[ ${1} == "update" ]]; then
    rm -f cache-page*.json
    cp meta.json meta.json.bak
    # A failed command must not leave some keys bumped and others stale.
    trap 'mv meta.json.bak meta.json' ERR
    commands=$(jq -r 'to_entries[] | [(.key),.value] | join("=")' < meta.json | grep '__command')
    while read -r line; do
        key="${line%%=*}"
        key="${key%__command}"
        command="${line#*=}"
        value=$(eval "${command}")
        json=$(cat meta.json)
        jq --sort-keys --arg key "$key" --arg value "$value" '.[$key] = $value' <<< "${json}" > meta.json
        echo "Result: [${key}] [${value}]"
    done <<< "${commands}"
    trap - ERR
    rm -f meta.json.bak cache-page*.json
fi

if [[ ${1} == "source" ]]; then
    lidarr_repo=$(jq -re '.lidarr_repo' < meta.json)
    lidarr_sha=$(jq -re '.lidarr_sha' < meta.json)
    pr_repo=$(jq -re '.pr_repo' < meta.json)
    pr_sha=$(jq -re '.pr_sha' < meta.json)
    rm -rf _src
    git clone --quiet --filter=blob:none --no-checkout "https://github.com/${lidarr_repo}.git" _src
    git -C _src fetch --quiet "https://github.com/${pr_repo}.git" "${pr_sha}"
    git -C _src checkout --quiet --detach "${lidarr_sha}"
    if ! git -C _src -c user.name=build -c user.email=build@localhost merge --quiet --no-edit "${pr_sha}"; then
        echo "${pr_repo}@${pr_sha:0:7} does not merge cleanly into ${lidarr_repo}@${lidarr_sha:0:7}: rebase the PR branch." >&2
        exit 1
    fi
fi

if [[ ${1} == "source" || ${1} == "migrations" ]]; then
    lidarr_repo=$(jq -re '.lidarr_repo' < meta.json)
    lidarr_sha=$(jq -re '.lidarr_sha' < meta.json)
    # The live database records migrations by number only, so a PR migration at or below upstream's newest would be skipped or collide.
    migrations=src/NzbDrone.Core/Datastore/Migration
    upstream_max=$(git -C _src ls-tree --name-only "${lidarr_sha}" "${migrations}/" | sed -n 's|.*/\([0-9][0-9]*\)_[^/]*\.cs$|\1|p' | sort -n | tail -n 1)
    pr_min=$(git -C _src diff --name-only --diff-filter=A "${lidarr_sha}" HEAD -- "${migrations}/" | sed -n 's|.*/\([0-9][0-9]*\)_[^/]*\.cs$|\1|p' | sort -n | sed -n 1p)
    if [[ -z ${upstream_max} ]]; then
        echo "No migrations found under ${migrations} in ${lidarr_repo}@${lidarr_sha:0:7}." >&2
        exit 1
    fi
    # 10# because 082 is not valid octal.
    if [[ -n ${pr_min} ]] && (( 10#${pr_min} <= 10#${upstream_max} )); then
        echo "PR migration ${pr_min} is not above upstream's ${upstream_max}: renumber it and fix the live database's VersionInfo first." >&2
        exit 1
    fi
    echo "Migrations: upstream ${lidarr_repo}@${lidarr_sha:0:7} up to ${upstream_max}, PR from ${pr_min:-none}"
fi

if [[ ${1} == "compile" ]]; then
    (cd _src && ./build.sh --backend --frontend --packages -r linux-musl-x64 -f net8.0)
    tar -czf lidarr.tar.gz -C _src/_artifacts/linux-musl-x64/net8.0 Lidarr
fi

if [[ ${1} == "amd64" ]]; then
    while IFS= read -r line; do
        opts+=(--build-arg "$line")
    done <<< "$(jq -r 'to_entries[] | [(.key | ascii_upcase),.value] | join("=")' < meta.json | grep -v '__COMMAND')"
    image=$(basename "$(git rev-parse --show-toplevel)" | tr '[:upper:]' '[:lower:]')
    docker build --platform "linux/${1}" -f "./linux-${1}.Dockerfile" -t "${image}-${1}" "${opts[@]}" .
fi

if [[ ${1} == "packages" ]]; then
    image=${2:?Usage: ./build.sh packages <image>}
    docker run --quiet --rm --entrypoint="" -v "${PWD}/":/repo -e PACKAGES="/repo/packages.txt" "${image}" sh -c 'if grep -q alpine < /etc/os-release; then apk upgrade --no-cache --available >/dev/null 2>&1 && apk info --format json --fields name,version 2>/dev/null | jq -re '\''.[] | [.name, .version] | join("=")'\'' | sort > ${PACKAGES}; else export DEBIAN_FRONTEND=noninteractive && apt-get update >/dev/null 2>&1 && apt-get upgrade -y >/dev/null 2>&1 && dpkg-query --showformat="\${Package}=\${Version}\n" --show 2>/dev/null | sort > ${PACKAGES}; fi'
    upstream_image=$(jq -re '.upstream_image' < meta.json | sed 's/ghcr.io\///g')
    upstream_tag=$(jq -re '.upstream_tag' < meta.json)
    upstream_tag_sha=$(jq -re '.upstream_tag_sha' < meta.json)
    upstream_sha=${upstream_tag_sha//${upstream_tag}-/}
    upstream_names=$(curl -fsSL "https://raw.githubusercontent.com/${upstream_image}/${upstream_sha}/packages.txt" | cut -d= -f1)
    packages_txt=$(cat packages.txt)
    rm -f packages.txt && touch packages.txt
    while read -r line; do
        package="${line%%=*}"
        grep -qxF "${package}" <<< "${upstream_names}" || echo "${line}" >> packages.txt
    done <<< "${packages_txt}"
fi

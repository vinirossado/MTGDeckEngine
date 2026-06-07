#!/usr/bin/env bash
set -euo pipefail

# Volume mount lands on top of /fuseki at runtime, so create the TDB2 location
# every boot. `mkdir -p` is a no-op on a populated dataset.
mkdir -p /fuseki/databases/mtg

# Install the permissive dev shiro.ini if the volume doesn't already have one.
# Fuseki picks it up from $FUSEKI_BASE/shiro.ini on startup.
if [ ! -f /fuseki/shiro.ini ]; then
    cp /opt/fuseki-conf/shiro.ini /fuseki/shiro.ini
fi

exec /opt/fuseki/fuseki-server "$@"

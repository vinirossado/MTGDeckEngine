# Minimal Fuseki image built from the official Apache Jena release.
# Why not a public image? The historical community images (stain, secoresearch)
# are unmaintained or impose their own dataset conventions. Apache does not
# publish an official jena-fuseki image to Docker Hub. So we just unpack the
# upstream tarball ourselves — fully reproducible and pinned to a version.
FROM eclipse-temurin:21-jre

ARG JENA_VERSION=5.1.0
ENV FUSEKI_HOME=/opt/fuseki \
    FUSEKI_BASE=/fuseki \
    PATH=/opt/fuseki:$PATH

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
COPY shiro.ini /opt/fuseki-conf/shiro.ini

RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates bash \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p "$FUSEKI_HOME" "$FUSEKI_BASE" /opt/fuseki-conf \
 && curl -fsSL "https://archive.apache.org/dist/jena/binaries/apache-jena-fuseki-${JENA_VERSION}.tar.gz" \
      | tar -xz --strip-components=1 -C "$FUSEKI_HOME" \
 && chmod +x "$FUSEKI_HOME/fuseki-server" /usr/local/bin/entrypoint.sh \
 && useradd --system --home "$FUSEKI_BASE" fuseki \
 && chown -R fuseki:fuseki "$FUSEKI_HOME" "$FUSEKI_BASE" /opt/fuseki-conf

USER fuseki
WORKDIR /fuseki
EXPOSE 3030

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
# Default args: persistent TDB2 dataset mounted at HTTP path /mtg.
CMD ["--tdb2", "--loc=/fuseki/databases/mtg", "--update", "/mtg"]

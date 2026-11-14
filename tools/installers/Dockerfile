# syntax=docker/dockerfile:1
ARG BASE=debian:13-slim
FROM ${BASE}
ARG DIST=debian
ARG DIST_VERSION=13
COPY package.deb /tmp/garbro.deb
COPY standard.xfl /tmp/standard.xfl
COPY probe.py /probe.py
COPY association_probe.py /association_probe.py
RUN --mount=type=secret,id=proxy_ca,mode=0444 \
    set -eu; \
    apt-get update; \
    apt-get install -y --no-install-recommends ca-certificates curl; \
    task_apt_ca=''; \
    if [ -f /run/secrets/proxy_ca ]; then \
      task_apt_ca='-o Acquire::https::CaInfo=/run/secrets/proxy_ca'; \
      curl --fail --cacert /run/secrets/proxy_ca "https://packages.microsoft.com/config/${DIST}/${DIST_VERSION}/packages-microsoft-prod.deb" -o /tmp/microsoft.deb; \
    else \
      curl --fail "https://packages.microsoft.com/config/${DIST}/${DIST_VERSION}/packages-microsoft-prod.deb" -o /tmp/microsoft.deb; \
    fi; \
    dpkg -i /tmp/microsoft.deb; \
    apt-get $task_apt_ca update; \
    apt-get $task_apt_ca install -y --no-install-recommends /tmp/garbro.deb python3 xvfb xauth x11-utils xdotool
ENV SDL_AUDIODRIVER=dummy
CMD ["python3", "/probe.py"]

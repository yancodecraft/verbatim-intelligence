# Tooling image for running Ansible (make configure) — Docker stays the
# only local dependency. Pinned base, pinned ansible-core and collections.
FROM ghcr.io/astral-sh/uv@sha256:eb2843a1e56fd9e30c7276ce1a52cba86e64c7b385f5e3279a0e08e02dd058fc

# DL3008: tooling image — openssh-client tracks the pinned base image;
# pinning the Debian point release breaks every rebuild for no gain here.
# hadolint ignore=DL3008
RUN apt-get update && apt-get install -y --no-install-recommends openssh-client \
    && rm -rf /var/lib/apt/lists/*

RUN uv tool install ansible-core==2.21.1
ENV PATH="/root/.local/bin:$PATH"

RUN ansible-galaxy collection install community.general:13.1.0 community.docker:5.2.1

WORKDIR /ansible

ENTRYPOINT ["ansible-playbook"]

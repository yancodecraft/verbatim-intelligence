# Tooling image for running Ansible (make configure) — Docker stays the
# only local dependency. Pinned base, pinned ansible-core and collections.
FROM ghcr.io/astral-sh/uv@sha256:7cf77f594be8042dab6daa9fe326f90962252268b4f120a7f5dccce4d947e6c1

RUN apt-get update && apt-get install -y --no-install-recommends openssh-client \
    && rm -rf /var/lib/apt/lists/*

RUN uv tool install ansible-core==2.21.1
ENV PATH="/root/.local/bin:$PATH"

RUN ansible-galaxy collection install community.general:13.1.0

WORKDIR /ansible

ENTRYPOINT ["ansible-playbook"]

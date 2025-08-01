#!/usr/bin/env python3

import argparse
import requests
import os
import subprocess
from typing import Iterable

PUBLISH_USERNAME = os.environ["PUBLISH_USERNAME"]
PUBLISH_PASSWORD = os.environ["PUBLISH_PASSWORD"]
VERSION = os.environ.get("GITHUB_SHA", "local-build")
FORK_ID = os.environ.get('FORK_ID', 'wega')

RELEASE_DIR = "tools"

#
# CONFIGURATION PARAMETERS
# Forks should change these to publish to their own infrastructure.
#
ROBUST_CDN_URL = "https://cdn.corvax-wega.ru/"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fork-id", default=FORK_ID)
    args = parser.parse_args()
    fork_id = args.fork_id

    session = requests.Session()
    session.auth = (
        os.environ["PUBLISH_USERNAME"],
        os.environ["PUBLISH_PASSWORD"]
    )

    print(f"Starting publish on your CDN for version {VERSION}")

    data = {
        "version": VERSION,
        "engineVersion": get_engine_version(),
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/start", json=data, headers=headers)
    resp.raise_for_status()
    print("Publish started, uploading files...")

    for file in get_files_to_publish():
        print(f"Publishing {file}")
        with open(file, "rb") as f:
            headers = {
                "Content-Type": "application/octet-stream",
                "Robust-Cdn-Publish-File": os.path.basename(file),
                "Robust-Cdn-Publish-Version": VERSION
            }
            resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/file", data=f, headers=headers)
            resp.raise_for_status()

    print("All files uploaded, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/finish", json=data, headers=headers)
    resp.raise_for_status()

    print("SUCCESS!")

def get_files_to_publish() -> Iterable[str]:
    for file in os.listdir(RELEASE_DIR):
        yield os.path.join(RELEASE_DIR, file)

def get_engine_version() -> str:
    proc = subprocess.run(["git", "describe", "--tags", "--abbrev=0"], stdout=subprocess.PIPE, cwd="RobustToolbox", check=True, encoding="UTF-8")
    tag = proc.stdout.strip()
    if tag.startswith("v"):
        tag = tag[1:] # Cut off v prefix.
    return tag

if __name__ == '__main__':
    main()

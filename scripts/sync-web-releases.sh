#!/usr/bin/env bash
# SalmonLauncher website — mirror GitHub Releases onto the site host.
#
# The website never sends visitors to GitHub for downloads. This script runs on
# the server, pulls the release list from the GitHub API, mirrors the installer
# and portable build into <web-root>/downloads/<tag>/, and rewrites
# <web-root>/assets/data/releases.json so the download page can render the
# latest package and the full version history from the local copy.
#
# Usage:  WEB_ROOT=/www/wwwroot/sl.hugomou.com ./sync-web-releases.sh
# Cron:   17 */2 * * * /bin/bash /root/sl-sync/sync-web-releases.sh >>/var/log/sl-sync.log 2>&1
#
# Optional: set GITHUB_TOKEN to raise the GitHub API rate limit.
# SPDX-License-Identifier: GPL-3.0-or-later

set -euo pipefail

WEB_ROOT="${WEB_ROOT:-/www/wwwroot/sl.hugomou.com}"
REPO="${REPO:-HugoMou/salmonlauncher}"
API="https://api.github.com/repos/${REPO}/releases?per_page=100"
UA="SalmonLauncher-site-sync"
DATA_FILE="${WEB_ROOT}/assets/data/releases.json"
DL_DIR="${WEB_ROOT}/downloads"

mkdir -p "$(dirname "$DATA_FILE")" "$DL_DIR"

API_JSON="$(mktemp)"
trap 'rm -f "$API_JSON"' EXIT

AUTH=()
if [ -n "${GITHUB_TOKEN:-}" ]; then
  AUTH=(-H "Authorization: Bearer ${GITHUB_TOKEN}")
fi

curl -fsSL --retry 3 --retry-delay 5 \
  -H "Accept: application/vnd.github+json" \
  -H "User-Agent: ${UA}" \
  ${AUTH[@]+"${AUTH[@]}"} \
  "$API" -o "$API_JSON"

python3 - "$API_JSON" "$WEB_ROOT" "$REPO" <<'PY'
import datetime
import hashlib
import json
import os
import re
import shutil
import sys
import urllib.request

api_path, web_root, repo = sys.argv[1], sys.argv[2], sys.argv[3]
dl_dir = os.path.join(web_root, "downloads")
data_path = os.path.join(web_root, "assets", "data", "releases.json")
ua = "SalmonLauncher-site-sync"

with open(api_path, "r", encoding="utf-8") as fh:
    releases = json.load(fh)

if not isinstance(releases, list):
    raise SystemExit("unexpected GitHub API response")


def hash_from_notes(body, name):
    if not body or not name:
        return ""
    pattern = r"^\s*" + re.escape(name) + r"\s+([A-Fa-f0-9]{64})\s*$"
    match = re.search(pattern, body, re.M)
    return match.group(1).upper() if match else ""


def download(url, dest):
    tmp = dest + ".part"
    req = urllib.request.Request(url, headers={"User-Agent": ua})
    with urllib.request.urlopen(req, timeout=240) as resp, open(tmp, "wb") as out:
        while True:
            chunk = resp.read(1024 * 256)
            if not chunk:
                break
            out.write(chunk)
    os.replace(tmp, dest)
    os.chmod(dest, 0o644)


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 256), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


items = []
tags = set()

for rel in releases:
    if rel.get("draft"):
        continue
    tag = rel.get("tag_name") or ""
    if not tag:
        continue

    body = rel.get("body") or ""
    assets = []

    for asset in rel.get("assets") or []:
        name = asset.get("name") or ""
        url = asset.get("browser_download_url") or ""
        size = int(asset.get("size") or 0)
        if not name or not url:
            continue
        if not (name.endswith(".exe") or name.endswith(".zip")):
            continue

        rel_dir = os.path.join(dl_dir, tag)
        os.makedirs(rel_dir, exist_ok=True)
        dest = os.path.join(rel_dir, name)

        if not os.path.exists(dest) or os.path.getsize(dest) != size:
            print("sync %s/%s" % (tag, name))
            download(url, dest)

        assets.append(
            {
                "name": name,
                "size": os.path.getsize(dest),
                "sha256": sha256_file(dest) or hash_from_notes(body, name),
                "url": "downloads/%s/%s" % (tag, name),
            }
        )

    if assets:
        tags.add(tag)
        items.append(
            {
                "tag": tag,
                "date": (rel.get("published_at") or "")[:10],
                "pre": bool(rel.get("prerelease")),
                "assets": assets,
            }
        )

# Drop mirrors for releases that no longer exist upstream.
if os.path.isdir(dl_dir):
    for entry in os.listdir(dl_dir):
        path = os.path.join(dl_dir, entry)
        if os.path.isdir(path) and entry not in tags:
            print("prune downloads/%s" % entry)
            shutil.rmtree(path)

sync_time = datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")

os.makedirs(os.path.dirname(data_path), exist_ok=True)
tmp = data_path + ".tmp"
with open(tmp, "w", encoding="utf-8") as fh:
    json.dump(
        {"repo": repo, "syncedAt": sync_time, "releases": items},
        fh,
        ensure_ascii=False,
        indent=2,
    )
    fh.write("\n")
os.replace(tmp, data_path)
os.chmod(data_path, 0o644)

print("wrote %s (%d releases, synced %s)" % (data_path, len(items), sync_time))
PY

#!/usr/bin/env python3
"""Write changelog.json for Lidarr's Updates page: this build and the previous successful ones."""
import base64
import datetime
import json
import os
import re
import sys
import urllib.request

API = "https://api.github.com"
BUILDS = 5
TITLE = re.compile(r"^- \*\*(.+?)\*\* — branch \[`(.+?)`\]", re.M)


def get(path):
    req = urllib.request.Request(API + path, headers={
        "Authorization": "Bearer " + os.environ["GH_TOKEN"],
        "Accept": "application/vnd.github+json",
    })
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.load(resp)


def file_at(repo, path, sha):
    return base64.b64decode(get(f"/repos/{repo}/contents/{path}?ref={sha}")["content"]).decode()


def titles(readme):
    return {branch: title for title, branch in TITLE.findall(readme)}


def upstream_lines(meta_old, meta_new):
    old, new = meta_old["lidarr_sha"], meta_new["lidarr_sha"]
    if old == new:
        return [], []
    commits = get(f"/repos/{meta_new['lidarr_repo']}/compare/{old}...{new}")["commits"]
    subjects = [c["commit"]["message"].splitlines()[0] for c in commits]
    added = [f"Upstream: {s[4:].strip()}" for s in subjects if s.startswith("New:")]
    fixed = [f"Upstream: {s[6:].strip()}" for s in subjects if s.startswith("Fixed:")]
    return added, fixed


def branch_lines(old, new, latest):
    old_branches, new_branches = old["meta"]["pr_branches"].split(), new["meta"]["pr_branches"].split()
    added, fixed = [], []
    for branch in new_branches:
        if branch not in old_branches:
            # A branch's README entry is often written after the build that added it.
            title = latest.get(branch) or new["titles"].get(branch) or branch
            (fixed if branch.startswith("fix/") else added).append(title)
    for branch in old_branches:
        if branch not in new_branches:
            fixed.append(f"Removed: {old['titles'].get(branch) or latest.get(branch) or branch}")
    return added, fixed


def build(run_number, date, meta, readme):
    return {"run": run_number, "date": date, "meta": json.loads(meta), "titles": titles(readme)}


def read(path):
    with open(path) as f:
        return f.read()


def main(out):
    repo = os.environ["GITHUB_REPOSITORY"]
    current = int(os.environ["LIDARRVERSION"].rsplit(".", 1)[1])
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    runs = get(f"/repos/{repo}/actions/workflows/build.yml/runs?branch=custom&status=success&per_page=30")["workflow_runs"]
    previous = [r for r in runs if r["run_number"] < current][:BUILDS]

    # This build's own files are the checkout; earlier builds' come from their commits.
    builds = [build(current, now, read("meta.json"), read("README.md"))]
    builds += [build(r["run_number"], r["created_at"], file_at(repo, "meta.json", r["head_sha"]), file_at(repo, "README.md", r["head_sha"]))
               for r in previous]

    entries = []
    for newer, older in zip(builds, builds[1:] + [None]):
        if len(entries) == BUILDS:
            break
        added, fixed = [], []
        if older:
            added, fixed = branch_lines(older, newer, builds[0]["titles"])
            up_added, up_fixed = upstream_lines(older["meta"], newer["meta"])
            added += up_added
            fixed += up_fixed
        entries.append({
            "version": f"10.0.0.{newer['run']}",
            "releaseDate": newer["date"],
            "branch": "custom",
            "changes": {"new": added, "fixed": fixed},
        })

    with open(out, "w") as f:
        json.dump(entries, f, indent=2, ensure_ascii=False)
    print(f"changelog: {len(entries)} builds, newest 10.0.0.{current}")


if __name__ == "__main__":
    main(sys.argv[1])

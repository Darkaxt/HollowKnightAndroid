"""Fail-closed, precompiler Android player member identity verification.

Authority is the adjacent repository manifest, derived from independently verified
archive-to-member bindings. A candidate directory is NOT evidence of provenance.
Changing authority requires a reviewed manifest and integrity-pin update together.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MANIFEST_SHA256 = "22bcc49ec73f8741b663e1dcbfdf47809083d5bc52be750c8a755beaf60c86ad"
PROFILE_VERSIONS = {"hollow-knight": "6000.0.61f1", "silksong": "6000.0.50f1"}


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate authority property: {key}")
        result[key] = value
    return result


def exact_keys(value, expected):
    if not isinstance(value, dict) or set(value) != set(expected):
        raise ValueError("Malformed authority properties")


def positive_integer(value):
    if type(value) is not int or value <= 0:
        raise ValueError("Malformed authority size")


def digest(value):
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
        raise ValueError("Malformed authority SHA256")


def load_authority():
    path = Path(__file__).resolve().with_name("unity-player-members.json")
    try:
        raw = path.read_bytes()
        if hashlib.sha256(raw).hexdigest() != MANIFEST_SHA256:
            raise ValueError("Trusted repository authority bytes changed")
        doc = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object)
        exact_keys(doc, ("schemaVersion", "profiles"))
        if type(doc["schemaVersion"]) is not int or doc["schemaVersion"] != 1:
            raise ValueError("Unsupported authority schema")
        exact_keys(doc["profiles"], PROFILE_VERSIONS)
        for profile, version in PROFILE_VERSIONS.items():
            authority = doc["profiles"][profile]
            exact_keys(authority, ("unityVersion", "provenance", "members"))
            if authority["unityVersion"] != version:
                raise ValueError("Wrong profile-specific Unity version")
            provenance = authority["provenance"]
            exact_keys(provenance, ("bindingSha256", "mainVerificationSha256", "archiveSha256",
                                   "archiveSize", "componentMember", "payloadSha256", "payloadSize",
                                   "payloadMemberListingSha256"))
            for key in ("bindingSha256", "mainVerificationSha256", "archiveSha256", "payloadSha256",
                        "payloadMemberListingSha256"):
                digest(provenance[key])
            for key in ("archiveSize", "payloadSize"):
                positive_integer(provenance[key])
            if provenance["componentMember"] != "TargetSupport.pkg.tmp/Payload":
                raise ValueError("Wrong archive component")
            members = authority["members"]
            if not isinstance(members, list) or not members:
                raise ValueError("Absent required member authority")
            names = set()
            for member in members:
                exact_keys(member, ("file", "archiveMember", "size", "sha256"))
                name = member["file"]
                if not isinstance(name, str) or re.fullmatch(r"UnityEngine\.[A-Za-z0-9]+\.dll", name) is None:
                    raise ValueError("Unsafe player member path")
                if name in names:
                    raise ValueError("Ambiguous player member authority")
                names.add(name)
                if member["archiveMember"] != "./Variations/il2cpp/Managed/" + name:
                    raise ValueError("Wrong archive member binding")
                positive_integer(member["size"])
                digest(member["sha256"])
            if "UnityEngine.CoreModule.dll" not in names:
                raise ValueError("Absent required CoreModule authority")
        return doc["profiles"]
    except (OSError, UnicodeError, ValueError, TypeError) as exc:
        raise ValueError(f"Unity player authority is invalid: {path} ({exc})") from exc


def verify_player(profile, player):
    authorities = load_authority()
    if profile not in PROFILE_VERSIONS:
        raise ValueError(f"Unknown profile: {profile}")
    authority = authorities[profile]
    version = authority["unityVersion"]
    player = Path(player)
    if not player.is_dir():
        raise ValueError(f"{profile} requires Android Unity {version}: managed directory is absent: {player}")
    for member in authority["members"]:
        path = player / member["file"]
        context = f"{profile} Android Unity {version} member {member['file']}"
        if not path.is_file():
            raise ValueError(f"{context} is missing: {path}")
        if path.stat().st_size != member["size"]:
            raise ValueError(f"{context} size mismatch: {path}")
        hash_state = hashlib.sha256()
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                hash_state.update(chunk)
        actual = hash_state.hexdigest()
        if actual != member["sha256"]:
            raise ValueError(f"{context} SHA256 mismatch: {path} ({actual})")
    return {"profile": profile, "unityVersion": version,
            "verifiedMembers": len(authority["members"]), "manifestSha256": MANIFEST_SHA256}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True)
    parser.add_argument("--player", required=True)
    args = parser.parse_args()
    try:
        result = verify_player(args.profile, args.player)
    except (OSError, ValueError) as exc:
        print(f"Unity player input rejected: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())

"""Build the VPM dependency that fixes SBP before Unity's first compilation."""
import argparse
import hashlib
import json
import tarfile
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

PACKAGE = Path(__file__).resolve().parents[1]
VERSION = "1.21.26"
UPSTREAM_SHA256 = "1b4ff6e04ab8ce9b2c6addc642a28f68c5b4b0f505f316669597e3c5ebb4243a"


def build(output):
    source = PACKAGE / "Tools~/Dependencies/com.unity.scriptablebuildpipeline-1.21.25.tgz"
    if hashlib.sha256(source.read_bytes()).hexdigest() != UPSTREAM_SHA256:
        raise ValueError("SBP source archive does not match the pinned Unity 1.21.25 release")
    entries = {}
    with tarfile.open(source) as archive:
        for member in archive.getmembers():
            if not member.isfile():
                continue
            name = member.name.removeprefix("package/")
            # Registry-only signature and upstream tests are not shipped as an embedded package.
            if name == ".signature" or name == "Tests.meta" or name.startswith("Tests/"):
                continue
            entries[name] = archive.extractfile(member).read()
    asmdef_path = "Editor/Unity.ScriptableBuildPipeline.Editor.asmdef"
    asmdef = json.loads(entries[asmdef_path])
    if asmdef["name"] != "Unity.ScriptableBuildPipeline.Editor" or asmdef["precompiledReferences"]:
        raise ValueError("Unexpected upstream SBP assembly references")
    asmdef["overrideReferences"] = True
    entries[asmdef_path] = (json.dumps(asmdef, indent=4) + "\n").encode()
    manifest = json.loads(entries["package.json"])
    manifest["version"] = VERSION
    manifest["displayName"] = "Scriptable Build Pipeline (LCGUdonSharp compatibility)"
    manifest["description"] += "\nLCGUdonSharp compatibility distribution based on Unity 1.21.25: excludes auto-referenced SDK DLLs from the editor assembly."
    entries["package.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    entries["CHANGELOG.md"] = (f"## [{VERSION}] - LCGUdonSharp compatibility\n"
                              "- Based on Unity Scriptable Build Pipeline 1.21.25.\n"
                              "- Exclude auto-referenced DLLs from the editor assembly to avoid the VRChat SDK global ExtensionMethods collision.\n\n").encode() + entries["CHANGELOG.md"]
    output.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(output, "w", ZIP_DEFLATED) as archive:
        for name, content in sorted(entries.items()):
            archive.writestr(name, content)
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    manifest = build(args.output)
    args.output.with_name("package.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Built {args.output}")

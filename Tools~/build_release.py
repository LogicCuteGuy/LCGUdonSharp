"""Build the installable VPM ZIP; a raw git archive is a developer checkout."""
import argparse
import json
import re
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

PACKAGE = Path(__file__).resolve().parents[1]
REQUIRED_GUIDS = {
    "Runtime/UdonSharp.Runtime.asmdef": "99835874ee819da44948776e0df4ff1d",
    "Editor/UdonSharp.Editor.asmdef": "84265b35cca3905448e623ef3903f0ff",
    "Runtime/Libraries/UdonSharp.Lib.asmdef": "e1a67d4778ffb214390ddafa61c1a557",
    "Editor/UdonSharpAssemblyDefinition.cs": "5136146375e9a0a498a72a0091b40cc1",
    "Editor/UdonSharpProgramAsset.cs": "c333ccfdd0cbdbc4ca30cef2dd6e6b9b",
    "UdonSharpLocator.asset": "de16ec4337e023649b01c85475dc06f9",
}


def imported_paths(root):
    return (path for path in sorted(root.rglob("*"))
            if not any(part.startswith(".") or part.endswith("~")
                       for part in path.relative_to(root).parts))


def validate_tree(root):
    guids = set()
    for path in [root, *imported_paths(root)]:
        if path.suffix == ".meta":
            match = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(), re.M)
            if not match or match[1] in guids:
                raise ValueError(f"Missing, invalid, or duplicate GUID: {path}")
            guids.add(match[1])
        elif not Path(str(path) + ".meta").is_file():
            raise ValueError(f"Missing metadata: {path}.meta")


REQUIRED_FEATURES = {
    "Runtime/UdonSharpAttributes.cs": "class LCGPacketAttribute",
    "Runtime/UdonSharpBehaviour.cs": "enum UdonExceptionKind",
    "Runtime/LCGBehaviours/LCGRuntime.cs": "class LCGRuntime",
    "Runtime/LCGBehaviours/LCGNetworkZone.cs": "class LCGNetworkZone",
    "Editor/Compiler/Lowering/CollectionSyntaxLowerer.cs": "class CollectionSyntaxLowerer",
    "Editor/Compiler/Binder/BoundNodes/BoundExceptionHandling.cs": "namespace UdonSharp.Compiler",
}


def validate_features(compiler):
    for relative, declaration in REQUIRED_FEATURES.items():
        source = compiler / relative
        if not source.is_file() or declaration not in source.read_text(encoding="utf-8-sig"):
            raise ValueError(f"LCGUdonSharp compiler feature is missing: {relative}. "
                             "Use the LCG compiler, not the stock SDK compiler.")


def build(package, output):
    compiler = package / "UdonSharp"
    validate_features(compiler)
    validate_tree(compiler)
    for relative, guid in REQUIRED_GUIDS.items():
        asset = compiler / relative
        if not asset.is_file() or f"guid: {guid}" not in Path(str(asset) + ".meta").read_text():
            raise ValueError(f"SDK GUID changed: {relative}")
    for name in ("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll",
                 "System.Reflection.Metadata.dll", "System.Text.Encoding.CodePages.dll"):
        dependency = compiler / "Runtime/Plugins" / name
        if not dependency.is_file() or not dependency.stat().st_size:
            raise ValueError(f"Missing compiler dependency: {name}")

    # Only bootstrap code is active on the first import. The SDK's compiler
    # must remain loadable until our installer swaps it for the hidden payload.
    entries = {}
    for source, destination in (("Editor", "Editor"), ("UdonSharp", "Payload~/UdonSharp"),
                                ("Example", "Samples~/Examples")):
        validate_tree(package / source)
        for path in imported_paths(package / source):
            if path.is_file():
                entries[f"{destination}/{path.relative_to(package / source).as_posix()}"] = path.read_bytes()
        entries[destination + ".meta"] = (package / (source + ".meta")).read_bytes()
    for name in ("README.md", "LICENSE.md", "CHANGELOG.md", "package.json.meta"):
        entries[name] = (package / name).read_bytes()
        if (package / (name + ".meta")).is_file():
            entries[name + ".meta"] = (package / (name + ".meta")).read_bytes()
    manifest = json.loads((package / "package.json").read_text())
    manifest["samples"] = [{"displayName": "LCGUdonSharp Examples",
                            "description": "Import after automatic compiler installation completes.",
                            "path": "Samples~/Examples"}]
    entries["package.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    output.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(output, "w", ZIP_DEFLATED) as archive:
        for name, contents in sorted(entries.items()):
            archive.writestr(name, contents)
    return len(entries)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(f"Built {args.output} ({build(PACKAGE, args.output)} files)")

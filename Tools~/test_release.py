import json
import shutil
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

import build_release


class ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = tempfile.TemporaryDirectory()
        cls.root = Path(cls.workspace.name)
        cls.archive = cls.root / "release.zip"
        build_release.build(build_release.PACKAGE, cls.archive)

    @classmethod
    def tearDownClass(cls):
        cls.workspace.cleanup()

    def test_first_import_contains_only_bootstrap_code(self):
        with ZipFile(self.archive) as archive:
            names = archive.namelist()
            self.assertFalse(any(name.startswith(("UdonSharp/", "Example/", "Tests/")) for name in names))
            active_code = [name for name in names if name.endswith((".cs", ".dll", ".asmdef"))
                           and not any(part.endswith("~") for part in name.split("/"))]
            self.assertTrue(active_code)
            self.assertTrue(all(name.startswith("Editor/") for name in active_code), active_code)
            self.assertIn("Payload~/UdonSharp/Runtime/Plugins/System.Text.Encoding.CodePages.dll", names)
            manifest = json.loads(archive.read("package.json"))
            self.assertIn(manifest["samples"][0]["path"] + "/LogicCuteGuy.LCGUdonSharp.Examples.asmdef", names)

    def test_archive_preserves_source_bytes_and_guids(self):
        with ZipFile(self.archive) as archive:
            for name in archive.namelist():
                if name.startswith("Payload~/UdonSharp/"):
                    source = build_release.PACKAGE / name.removeprefix("Payload~/")
                    self.assertEqual(archive.read(name), source.read_bytes(), name)
            for path, guid in build_release.REQUIRED_GUIDS.items():
                self.assertIn(guid.encode(), archive.read(f"Payload~/UdonSharp/{path}.meta"))

    def test_missing_meta_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            tree = Path(directory) / "Compiler"
            tree.mkdir()
            Path(str(tree) + ".meta").write_text("guid: " + "1" * 32)
            (tree / "Behaviour.cs").write_text("class Behaviour {}")
            with self.assertRaisesRegex(ValueError, "Missing metadata"):
                build_release.validate_tree(tree)

    def test_missing_lcg_feature_is_rejected_before_zip_created(self):
        for relative in build_release.REQUIRED_FEATURES:
            with self.subTest(feature=relative), tempfile.TemporaryDirectory() as directory:
                package = Path(directory)
                shutil.copytree(build_release.PACKAGE / "UdonSharp", package / "UdonSharp")
                shutil.copy2(build_release.PACKAGE / "UdonSharp.meta", package / "UdonSharp.meta")
                (package / "UdonSharp" / relative).unlink()
                output = package / "invalid.zip"
                with self.assertRaisesRegex(ValueError, "compiler feature is missing"):
                    build_release.build(package, output)
                self.assertFalse(output.exists())

    def test_stock_attributes_are_rejected_even_with_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory)
            shutil.copytree(build_release.PACKAGE / "UdonSharp", package / "UdonSharp")
            shutil.copy2(build_release.PACKAGE / "UdonSharp.meta", package / "UdonSharp.meta")
            (package / "UdonSharp/Runtime/UdonSharpAttributes.cs").write_text(
                "namespace UdonSharp { public class UdonSyncedAttribute : System.Attribute {} }")
            output = package / "invalid.zip"
            with self.assertRaisesRegex(ValueError, "compiler feature is missing"):
                build_release.build(package, output)
            self.assertFalse(output.exists())

    def test_changed_sdk_guid_is_rejected_before_zip_created(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory)
            shutil.copytree(build_release.PACKAGE / "UdonSharp", package / "UdonSharp")
            shutil.copy2(build_release.PACKAGE / "UdonSharp.meta", package / "UdonSharp.meta")
            meta = package / "UdonSharp/Runtime/UdonSharp.Runtime.asmdef.meta"
            meta.write_text(meta.read_text().replace(build_release.REQUIRED_GUIDS["Runtime/UdonSharp.Runtime.asmdef"], "0" * 32))
            output = package / "invalid.zip"
            with self.assertRaisesRegex(ValueError, "SDK GUID changed"):
                build_release.build(package, output)
            self.assertFalse(output.exists())

    def test_missing_dll_is_rejected_before_zip_created(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory)
            shutil.copytree(build_release.PACKAGE / "UdonSharp", package / "UdonSharp")
            shutil.copy2(build_release.PACKAGE / "UdonSharp.meta", package / "UdonSharp.meta")
            (package / "UdonSharp/Runtime/Plugins/System.Text.Encoding.CodePages.dll").unlink()
            output = package / "invalid.zip"
            with self.assertRaisesRegex(ValueError, "Missing compiler dependency"):
                build_release.build(package, output)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()

"""Dependency collection through the CLI, using synthetic PK3s and loose roots."""
import os
import struct
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path

from test_aas import EXE


def map_bytes(material, entities):
    lumps = [b""] * 18
    lumps[0] = ("\n".join("{\n" + "\n".join(f'"{k}" "{v}"' for k, v in entity.items())
                          + "\n}" for entity in entities) + "\n\0").encode()
    lumps[1] = struct.pack("<64sii", material.encode(), 0, 0)
    lumps[13] = struct.pack("<iii92x", 0, -1, 1)
    offsets = []
    offset = 8 + 8 * len(lumps)
    for lump in lumps:
        offsets.append(struct.pack("<II", offset, len(lump)))
        offset += len(lump)
    return b"IBSP" + struct.pack("<I", 47) + b"".join(offsets + lumps)


def md3(material):
    header = bytearray(108)
    struct.pack_into("<4sI", header, 0, b"IDP3", 15)
    struct.pack_into("<I", header, 84, 1)
    struct.pack_into("<II", header, 100, 108, 284)
    surface = bytearray(108)
    struct.pack_into("<I", surface, 76, 1)
    struct.pack_into("<I", surface, 92, 108)
    struct.pack_into("<I", surface, 104, 176)
    return header + surface + struct.pack("<64sI", material.encode(), 0)


def write_pk3(path, entries):
    with zipfile.ZipFile(path, "w") as package:
        for name, data in entries.items():
            package.writestr(name, data)


class MapPackaging(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source = self.root / "source.pk3"
        self.shared = self.root / "shared.pk3"
        self.base = self.root / "target" / "baseq3"
        self.base.mkdir(parents=True)

    def convert(self, target=46, *options):
        command = [str(EXE)] if os.name == "nt" else ["mono", str(EXE)]
        result = subprocess.run(command + [str(target), str(self.source), *map(str, options)],
                                cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return zipfile.ZipFile(self.root / f"source_IBSP{target}.pk3"), result.stdout

    def test_graph_and_shader_compatibility(self):
        shader = """textures/demo/terrain {
    // novlcollapse in a comment
    NoVLCollapse
    skyParms env/testsky 512 -
    { rgbGen identity map "textures/demo/novlcollapse.tga" }
    { animMap 3 textures/demo/f1 textures/demo/f2 }
    { videoMap testdemo.roq }
    unknownDirective "novlcollapse"
}
textures/demo/unused { { map textures/demo/unrelated } }
"""
        entities = [{"classname": "worldspawn", "music": "music/demo.wav"},
                    {"classname": "func_plat", "model": "*1", "model2": "models/demo.md3"},
                    {"classname": "misc_model", "skin": "models/demo.skin"},
                    {"classname": "weapon_custom"}, {"classname": "target_speaker", "noise": "sound/testmissing.wav"}]
        write_pk3(self.source, {
            "maps/demo.bsp": map_bytes("textures/demo/terrain", entities),
            "maps/other.bsp": map_bytes("textures/demo/unused", entities[:1]),
            "scripts/shared.shader": shader, "textures/demo/f1.jpg": b"input wins",
            "scripts/shared.arena": '{ map "other" longname "Other" }\n{ map "demo" longname "Demo" }',
            "models/demo.md3": md3("textures/demo/model"), "models/demo.skin": "body,textures/demo/skin\n",
            "maps/demo/lm_0000.tga": b"lm", "default.cfg": b"global", "vm/qagame.qvm": b"game",
        })
        fallback = self.base / "pak0.pk3"
        write_pk3(fallback, {"scripts/shared.shader": "textures/demo/model { { map textures/demo/modelimage } }",
                             "textures/demo/modelimage.jpg": b"model", "textures/demo/skin.tga": b"skin",
                             "textures/demo/f2.tga": b"target", "scripts/ignored.shader": "unused {}"})
        sky = {"env/testsky_" + side + ".tga": b"sky" for side in ("rt", "lf", "ft", "bk", "up", "dn")}
        write_pk3(self.shared, dict(sky, **{
            "music/demo.ogg": b"track", "textures/demo/novlcollapse.jpg": b"diffuse",
            "Textures/Demo/F2.JPG": b"first root", "textures/demo/f1.jpg": b"shadowed",
            "video/testdemo.roq": b"video", "levelshots/demo.jpg": b"levelshot",
            "sound/movers/plats/pt1_strt.wav": b"lift", "sound/movers/plats/pt1_end.wav": b"lift",
        }))
        later = self.root / "loose"
        (later / "textures/demo").mkdir(parents=True)
        (later / "textures/demo/f2.jpg").write_bytes(b"later root")
        for target in (46, 47):
            package, log = self.convert(target, "--bsp:demo.bsp", "--asset-root", self.shared,
                                        "--asset-root", later, "--target-base", self.base.parent)
            with package:
                files = set(package.namelist())
                self.assertTrue(set(sky).issubset(files))
                for name in ("levelshots/demo.jpg", "music/demo.ogg", "video/testdemo.roq",
                             "models/demo.md3", "models/demo.skin", "textures/demo/skin.tga",
                             "textures/demo/modelimage.jpg", "maps/demo/lm_0000.tga",
                             "sound/movers/plats/pt1_strt.wav", "sound/movers/plats/pt1_end.wav"):
                    self.assertIn(name, files)
                self.assertEqual(package.read("textures/demo/f1.jpg"), b"input wins")
                self.assertEqual(package.read("Textures/Demo/F2.JPG"), b"first root")
                self.assertFalse({"maps/other.bsp", "default.cfg", "vm/qagame.qvm", "textures/demo/f2.tga"} & files)
                script = package.read(next(n for n in files if n.endswith(".shader"))).decode()
                self.assertIn("// novlcollapse in a comment", script)
                self.assertIn('"textures/demo/novlcollapse.tga"', script)
                self.assertIn('unknownDirective "novlcollapse"', script)
                self.assertEqual("NoVLCollapse" in script, target == 47)
                self.assertNotIn("textures/demo/unused", script)
                arena = package.read(next(n for n in files if n.endswith(".arena")))
                self.assertIn(b'"Demo"', arena)
                self.assertNotIn(b'"Other"', arena)
            for detail in ("weapon_custom", "sound/testmissing.wav", "maps/demo.bsp -> target_speaker",
                           "unknownDirective".lower(), str(self.shared)):
                self.assertIn(detail, log)

    def test_union_and_aas_patch(self):
        entities = [{"classname": "worldspawn"}]
        write_pk3(self.source, {"maps/one.bsp": map_bytes("textures/demo/common", entities),
                                "maps/two.bsp": map_bytes("textures/demo/common", entities),
                                "textures/demo/common.tga": b"common", "unused.txt": b"discard"})
        package, _ = self.convert()
        with package:
            self.assertCountEqual(package.namelist(), ["maps/one.bsp", "maps/two.bsp", "textures/demo/common.tga"])
        write_pk3(self.source, {"maps/one.aas": b"overlay"})
        package, _ = self.convert()
        with package:
            self.assertEqual(package.read("maps/one.aas"), b"overlay")

    def test_workshop_autodiscovery_and_stock_package_order(self):
        apps = self.root / "steamapps"
        stock = apps / "common" / "Quake Live" / "baseq3"
        stock.mkdir(parents=True)
        self.source = apps / "workshop/content/282440/123/custom.pk3"
        self.source.parent.mkdir(parents=True)
        write_pk3(self.source, {"maps/test.bsp": map_bytes("textures/autotest/custom", [
            {"classname": "worldspawn", "music": "music/autotest.wav"}])})
        write_pk3(stock / "pak00.pk3", {"textures/autotest/custom.jpg": b"old"})
        write_pk3(stock / "pak01.pk3", {"textures/autotest/custom.jpg": b"patch"})
        write_pk3(self.base / "pak0.pk3", {"music/autotest.ogg": b"target music"})
        write_pk3(self.base / "unrelated.pk3", {"music/autotest.ogg": b"unrelated mod"})
        command = [str(EXE)] if os.name == "nt" else ["mono", str(EXE)]
        subprocess.run(command + ["46", str(self.source), "--target-base", str(self.base)],
                       cwd=self.root, check=True, capture_output=True)
        with zipfile.ZipFile(self.root / "custom_IBSP46.pk3") as package:
            self.assertEqual(package.read("textures/autotest/custom.jpg"), b"patch")
            self.assertEqual(package.read("music/autotest.ogg"), b"target music")

    def test_skeletal_model_materials(self):
        material = b"textures/demo/skin"
        iqm = bytearray(124)
        end = 124 + 24 + len(material) + 1
        struct.pack_into("<16s7I", iqm, 0, b"INTERQUAKEMODEL", 2, end, 0,
                         len(material) + 1, 148, 1, 124)
        iqm += bytes(24) + material + b"\0"
        mdr = bytearray(104 + 12 + 168)
        struct.pack_into("<4sI", mdr, 0, b"RDM5", 2)
        struct.pack_into("<5i", mdr, 84, 1, 104, 0, 0, len(mdr))
        struct.pack_into("<3i", mdr, 104, 1, 12, 180)
        struct.pack_into("<64s", mdr, 116 + 68, material + b"2")
        struct.pack_into("<i", mdr, 116 + 164, 168)
        write_pk3(self.source, {"maps/demo.bsp": map_bytes("noshader", [
            {"classname": "misc_model", "model": "models/demo.iqm", "model2": "models/demo.mdr"}]),
            "models/demo.iqm": iqm, "models/demo.mdr": mdr, "textures/demo/skin.png": b"a",
            "textures/demo/skin2.png": b"b"})
        package, log = self.convert()
        with package:
            self.assertIn("textures/demo/skin.png", package.namelist())
            self.assertIn("textures/demo/skin2.png", package.namelist())
        self.assertNotIn("Warning:", log)


if __name__ == "__main__":
    unittest.main()

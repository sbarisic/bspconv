"""QAA fixtures contain generated geometry, lighting and an uncompressed LZX block."""
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
import zipfile

from test_aas import EXE, aas, bsp, SOURCE


def assemble(lumps):
    offset = 152
    entries = []
    for data in lumps:
        entries.append(struct.pack("<ii", offset, len(data)))
        offset += len(data)
    return b"IBSP/\0\0\0" + b"".join(entries + lumps)


def lump(data, number):
    pos, length = struct.unpack_from("<ii", data, 8 + 8 * number)
    return data[pos:pos + length]


def page(which):
    row = bytes(channel for x in range(512) for channel in (x % 256, 60, 7 + which))
    rows = []
    for y in range(512):
        pixels = bytearray(row)
        pixels[1::3] = bytes([y % 256]) * 512
        rows.append(pixels)
    return b"".join(rows)


def lighting_bsp():
    lumps = [b""] * 18
    lumps[0] = b'{\n"classname" "worldspawn"\n}\n\0'
    lumps[1] = b"".join(struct.pack("<64sii", name, 0x24, 16) for name in (b"textures/test/lit", b"textures/test/plain"))
    lumps[13] = b"".join(struct.pack("<8i72x", texture, -1, 1, 0, 0, 0, 0, light)
                         for texture, light in [(0, 0), (0, 1), (1, 1), (0, -3)])
    lumps[14] = page(0) + page(1)
    return assemble(lumps)


def lzx_stored(payload):
    # One LZX uncompressed block with the three initial repeat offsets.
    bits = "0" + "011" + f"{len(payload):024b}" + "0000"
    packed = struct.pack("<HHIII", int(bits[:16], 2), int(bits[16:], 2), 1, 1, 1) + payload
    block = struct.pack(">H", len(packed)) + packed
    header = struct.pack(">12I", 0x0ff512ee, 0x01030000, 0, 0, 32768, 32768,
                         0, len(payload), 0, len(block)+4, 32768, len(block))
    return header + struct.pack(">I", len(block)) + block


def bpk(records):
    offset = 8 + 24 * len(records)
    directory, contents = [], []
    for identifier, data, compress in records:
        stored = lzx_stored(data) if compress else data
        directory.append(struct.pack(">QIIII", identifier, offset, len(data), int(compress), len(stored)))
        contents.append(stored)
        offset += len(stored)
    return struct.pack(">II", 1, len(records)) + b"".join(directory + contents)


class QaaTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)

    def convert(self, *args, success=True):
        command = [str(EXE)] if os.name == "nt" else ["mono", str(EXE)]
        run = subprocess.run(command + list(args), cwd=self.root, capture_output=True, text=True)
        self.assertEqual(run.returncode == 0, success, run.stdout + run.stderr)

    def test_loose_lighting_and_source_selection(self):
        source = self.root / "fixture.bsp"
        source.write_bytes(lighting_bsp())
        self.convert("46", source.name)
        out = self.root / "fixture_IBSP46.bsp"
        self.assertEqual(lump(out.read_bytes(), 14), lump(source.read_bytes(), 14))
        self.convert("46", "--source", "qaa", source.name)
        lm = lump(out.read_bytes(), 14)
        self.assertEqual(len(lm), 2*128*128*3)
        self.assertEqual(lm[:6], bytes([2, 2, 7, 6, 2, 7]))
        self.assertEqual(lm[49152+384:49152+387], bytes([2, 6, 8]))
        good = out.read_bytes()
        broken = bytearray(source.read_bytes())
        struct.pack_into("<i", broken, 12+14*8, 123)
        source.write_bytes(broken)
        self.convert("46", "--source", "qaa", source.name, success=False)
        self.assertEqual(out.read_bytes(), good)

    def test_full_detail_and_material_variants(self):
        src = lighting_bsp()
        with zipfile.ZipFile(self.root/"test.pk3", "w") as z:
            z.writestr("maps/fixture.bsp", src)
            z.writestr("scripts/test.shader", "textures/test/lit { surfaceparm metalsteps\n{ map $lightmap\n tcMod scale 1 1 }\n{ map textures/test/base.tga\n blendFunc filter } }")
            z.writestr("textures/test/base.tga", b"texture")
            z.writestr("textures/test/plain.tga", b"texture")
        self.convert("47", "--source", "qaa", "test.pk3")
        with zipfile.ZipFile(self.root/"test_IBSP47.pk3") as z:
            out = z.read("maps/fixture.bsp")
            textures = lump(out,1)
            self.assertEqual(textures[:144], lump(src,1))
            faces = list(struct.iter_unpack("<8i72x", lump(out,13)))
            self.assertEqual([f[0] for f in faces], [2,3,4,0])
            self.assertEqual(faces[-1][7], -3)
            shader = z.read(next(n for n in z.namelist() if n.startswith("scripts/qaa_"))).decode()
            self.assertEqual(shader.count("tcMod scale"), 2)
            self.assertEqual(shader.count("tcGen lightmap"), 3)
            self.assertIn("map textures/test/plain", shader)
            for which in range(2):
                name = next(n for n in z.namelist() if n.endswith(f"/lm_{which}.tga"))
                image = z.read(name)
                self.assertEqual(struct.unpack_from("<HHB", image,12),(512,512,24))
                self.assertEqual(len(image),18+512*512*3)
                self.assertEqual(image[17], 0)
                for x,y in [(0,0), (511,511), (193,417), (2,252)]:
                    pos = 18 + ((511-y)*512+x)*3
                    self.assertEqual(image[pos:pos+3], bytes([7+which,y%256,x%256]))

    def test_lit_page_referenced_by_a_diffuse_only_material(self):
        bsp_data = lighting_bsp()
        lumps = [lump(bsp_data, i) for i in range(18)]
        lumps[13] = lumps[13][:104]
        with zipfile.ZipFile(self.root / "diffuse.pk3", "w") as z:
            z.writestr("maps/diffuse.bsp", assemble(lumps))
            z.writestr("scripts/plain.shader", "textures/test/lit { { map textures/test/base.tga } }")
            z.writestr("textures/test/base.tga", b"diffuse")
        self.convert("46", "--source", "qaa", "diffuse.pk3")
        with zipfile.ZipFile(self.root / "diffuse_IBSP46.pk3") as z:
            out = z.read("maps/diffuse.bsp")
            self.assertEqual(lump(out, 13), lumps[13])
            self.assertEqual(lump(out, 1), lumps[1])
            self.assertEqual(sum(n.endswith("/lm_0.tga") for n in z.namelist()), 1)
            self.assertFalse(any("/lit_" in n for n in z.namelist()))

    def test_bpk_stored_compressed_and_checksum_pairing(self):
        # Known retail identifiers for maps/qaadm18.bsp and maps/qaadm15.aas.
        # The different basenames force pairing by checksum.
        data = bsp(47)
        nav = aas(5, SOURCE[47])
        path = self.root/"pak0.bpk"
        path.write_bytes(bpk([(0x3e925f99d8b4935b,data,True),(0x3e925f94d8b78158,nav,False)]))
        self.convert("46", path.name, "--bsp:catalyst.bsp")
        out = self.root/"pak0_IBSP46.pk3"
        with zipfile.ZipFile(out) as z:
            self.assertIn("maps/catalyst.aas",z.namelist())
            self.assertEqual(z.read("maps/catalyst.aas")[124:],nav[124:])
        before = out.read_bytes()
        broken = bytearray(path.read_bytes())
        struct.pack_into(">I",broken,16,0xffffffff)
        path.write_bytes(broken)
        self.convert("46",path.name,success=False)
        self.assertEqual(out.read_bytes(),before)

    def test_xbox_texture_formats(self):
        def dds(fourcc, pixels):
            header = bytearray(128)
            header[:4] = b"DDS "
            struct.pack_into("<I", header, 4, 124)
            struct.pack_into("<II", header, 12, 1, 1)
            struct.pack_into("<I", header, 76, 32)
            if fourcc:
                header[84:88] = fourcc.encode()
            else:
                struct.pack_into("<5I", header, 88, 32, 0x00ff0000, 0xff00, 0xff, 0xff000000)
            return header + pixels

        colors = struct.pack("<HHI", 0xf800, 0, 0)
        cases = {
            "transparent": ("DXT1", struct.pack("<HHI", 0, 0xffff, 3), (0, 0, 0, 0)),
            "premultiplied": ("DXT2", bytes([4])+bytes(7)+struct.pack("<HHI", 0x2000, 0, 0), (0, 0, 124, 68)),
            "alpha4": ("DXT3", bytes([7])+bytes(7)+colors, (0, 0, 255, 119)),
            "alpha8": ("DXT5", bytes([240, 30, 2])+bytes(5)+colors, (0, 0, 255, 210)),
            "alpha6": ("DXT5", bytes([10, 80, 7])+bytes(5)+colors, (0, 0, 255, 255)),
            "rgba": ("", bytes([1, 5, 11, 23]), (1, 5, 11, 23)),
        }
        with zipfile.ZipFile(self.root / "media.pk3", "w") as z:
            z.writestr("maps/texture.bsp", lighting_bsp())
            frames = " ".join("textures/test/" + n + ".dds" for n in cases)
            z.writestr("scripts/m.shader", "textures/test/lit\n{\n{\nanimMap 3 " + frames + "\n}\n}")
            for name, (fourcc, pixels, _) in cases.items():
                z.writestr("textures/test/" + name + ".dds", dds(fourcc, pixels))
        self.convert("46", "--source", "qaa", "media.pk3")
        with zipfile.ZipFile(self.root / "media_IBSP46.pk3") as z:
            for name, (_, _, expected) in cases.items():
                image = z.read("textures/test/" + name + ".tga")
                self.assertEqual(image[16:18], bytes([32, 8]), name)
                self.assertEqual(tuple(image[18:]), expected, name)

    def test_bpk_rejects_damaged_lzx_and_conflicting_ids(self):
        raw = bsp(47)
        key = 0x3e925f99d8b4935b
        path = self.root / "broken.bpk"
        output = self.root / "broken_IBSP46.pk3"
        path.write_bytes(bpk([(key, raw, False)]))
        self.convert("46", path.name)
        original = output.read_bytes()
        bad_frame = bytearray(bpk([(key, raw, True)]))
        struct.pack_into(">I", bad_frame, 32+48, 0xffffffff)
        overlap = bytearray(bpk([(key, raw, False), (123, raw, False)]))
        struct.pack_into(">I", overlap, 40, 56)
        stale = bpk([(key, raw, False), (0x3e925f99d8b78158, aas(5, 0), False)])
        for corrupt in (bad_frame, overlap, bpk([(key, raw, False), (key, b"x", False)]), stale):
            path.write_bytes(corrupt)
            self.convert("46", path.name, success=False)
            self.assertEqual(output.read_bytes(), original)

"""Check local showcase links, asset sizes and sample DOCX structure."""
from pathlib import Path
from urllib.parse import unquote, urlsplit
from zipfile import ZipFile
from xml.etree import ElementTree as ET
from PIL import Image
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
W = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


def check_headings(package, document, expected):
    styles = ET.fromstring(package.read("word/styles.xml"))
    heading_styles = {}
    for style in styles.findall(W + "style"):
        name = style.find(W + "name")
        if name is not None and name.get(W + "val").lower() in ("heading 1", "heading 2"):
            level = int(name.get(W + "val")[-1])
            outline = style.find(W + "pPr/" + W + "outlineLvl")
            assert outline is not None and int(outline.get(W + "val")) == level - 1
            heading_styles[style.get(W + "styleId")] = level
    headings = []
    for paragraph in document.findall(".//" + W + "p"):
        style = paragraph.find(W + "pPr/" + W + "pStyle")
        if style is not None and style.get(W + "val") in heading_styles:
            headings.append((heading_styles[style.get(W + "val")], "".join(paragraph.itertext())))
    assert headings == expected, headings


def main():
    documents = [ROOT / "README.md", ROOT / "README.en.md", ROOT / "CONTRIBUTING.md", ROOT / "CHANGELOG.md"]
    documents += [ROOT / "docs" / name for name in ("开发指南.md", "开发计划与验收.md", "模板适配说明.md", "依赖与发布准备.md", "Pandoc源码材料核对.md", "目标电脑与用户试用.md", "展示验证记录.md")]
    documents += list((ROOT / "examples/showcase").glob("*.md"))
    count = 0
    for path in documents:
        text = path.read_text(encoding="utf-8-sig")
        for link in re.findall(r"!?\[[^\]]*\]\(([^\n)]+)\)", text):
            parsed = urlsplit(link)
            if parsed.scheme:
                continue
            target = (path.parent / unquote(parsed.path)).resolve() if parsed.path else path
            assert target.exists(), f"Missing link in {path}: {link}"
            if parsed.fragment and target.suffix == ".md":
                headings = [line.lstrip("# ").strip().lower().replace(" ", "-") for line in target.read_text(encoding="utf-8-sig").splitlines() if line.startswith("#")]
                assert unquote(parsed.fragment).lower() in headings, f"Missing anchor: {link}"
            count += 1

    assets = ROOT / "docs/assets"
    with Image.open(assets / "demo.gif") as image:
        frames, duration = image.n_frames, 0
        for i in range(frames):
            image.seek(i)
            duration += image.info["duration"]
        assert frames == 5 and duration == 24000
    with Image.open(assets / "social-preview.png") as image:
        assert image.size == (1280, 640)
    assert (assets / "social-preview.png").stat().st_size < 1_000_000

    manifest = json.loads((assets / "showcase.json").read_text(encoding="utf-8"))
    assert manifest["demoFrames"] == frames
    assert not any("查看模板规则" in step for step in manifest["demoSteps"])
    assert Path(manifest["samples"][0]["input"]).stem == "work-report"
    image_source = manifest["imageSource"]
    provided_image_hash = hashlib.sha256((ROOT / image_source["path"]).read_bytes()).hexdigest()
    assert provided_image_hash == image_source["sha256"]
    assert provided_image_hash == "3e26c78622ac207cedf757a5d999494918c736380b0115dff0a8186c7b2e41b3"
    structures = []
    for sample in manifest["samples"]:
        for kind in ("input", "docx", "page"):
            actual = hashlib.sha256((ROOT / sample[kind]).read_bytes()).hexdigest()
            assert actual == sample[kind + "Sha256"], sample[kind]
        with ZipFile(ROOT / sample["docx"]) as package:
            document = ET.fromstring(package.read("word/document.xml"))
            settings = ET.fromstring(package.read("word/settings.xml"))
            fields = settings.find(W + "updateFields")
            assert fields is None or fields.get(W + "val") in ("false", "0", "off")
            core = ET.fromstring(package.read("docProps/core.xml"))
            for element in core:
                if element.tag.split("}")[-1] in ("creator", "lastModifiedBy"):
                    assert not (element.text or "").strip()
            assert not any(name.startswith("word/fonts/") for name in package.namelist())
            text = "".join(document.itertext())
            stem = Path(sample["input"]).stem
            if stem == "numbering":
                assert len(document.findall(".//" + W + "numPr")) >= 14
                assert "检查两位数编号后的正文与续行能否对齐" in text
            if stem == "work-report":
                assert "协作文档库试运行周报" in text and "虚构工作场景" in text
                assert "18条反馈" in text and "14条" in text and "剩余4条" in text
                check_headings(package, document, [
                    (1, "1 本周概况"), (1, "2 工作进展"),
                    (1, "3 下周安排"), (2, "3.1 处理剩余反馈"),
                    (2, "3.2 复查使用流程"),
                ])
                assert "风险与处理" not in text and "命名不统一" not in text and "说明不完整" not in text
                assert "负责人、处理说明和复查结果" in text and "历史版本仍可追溯" in text
                assert len(document.findall(".//" + W + "tbl")) == 1
                assert len(document.findall(".//" + W + "tr")) == 5
            if stem == "table":
                assert len(document.findall(".//" + W + "tbl")) == 1
                assert len(document.findall(".//" + W + "tr")) == 7
                assert document.find(".//" + W + "tblHeader") is not None
            if stem == "mixed-images":
                check_headings(package, document, [
                    (1, "1 内容与单位"), (1, "2 本地图片"), (1, "3 链接与代码"),
                ])
                for token in ("80µm", "≤5", "0.60 L/min", "0xFF", "0b1010", "sample_count = 12"):
                    assert token in text, token
                assert "CAN ID" not in text
                assert any(name.startswith("word/media/") for name in package.namelist())
                assert any(hashlib.sha256(package.read(name)).hexdigest() == provided_image_hash for name in package.namelist() if name.startswith("word/media/")), "用户原图未原样嵌入DOCX"
                assert b"https://pandoc.org/" in package.read("word/_rels/document.xml.rels")
            structures.append(stem)
    report = {"localLinksChecked": count, "brokenLinks": 0, "demoFrames": frames, "demoDurationSeconds": duration / 1000, "socialPreview": "1280x640, under 1 MB", "docxStructuralChecks": structures, "claims": "结构检查不替代Word页面检查或GitHub远程执行"}
    target = ROOT / "artifacts/showcase/validation.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

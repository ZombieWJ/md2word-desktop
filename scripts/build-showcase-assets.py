"""Compose repository artwork from actual WinForms frames and Word page exports."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import argparse
import hashlib
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "docs/assets"
WORK = ROOT / "artifacts/showcase"
FONT = "C:/Windows/Fonts/msyh.ttc"
BOLD = "C:/Windows/Fonts/msyhbd.ttc"


def font(size, bold=False):
    return ImageFont.truetype(BOLD if bold else FONT, size)


def wrapped(text, width, face):
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    result = []
    for line in text.splitlines():
        current = ""
        for char in line:
            if current and probe.textlength(current + char, font=face) > width:
                result.append(current)
                current = ""
            current += char
        result.append(current)
    return result


def fit(image, box):
    image = image.copy().convert("RGB")
    image.thumbnail(box, Image.Resampling.LANCZOS)
    return image


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--word-render-dir", type=Path, default=WORK / "word-render")
    args = parser.parse_args()
    render_root = args.word_render_dir.resolve()
    ASSETS.mkdir(parents=True, exist_ok=True)
    source = ROOT / "examples/showcase"
    word_records = json.loads((render_root / "word-render.json").read_text(encoding="utf-8-sig"))
    records = []
    for stem, title, output_name in [("work-report", "工作文档转换示例 · 虚构试运行周报", "work-report.docx"), ("table", "中文表格", "table.docx"), ("mixed-images", "中英文与用户提供的图片", "mixed-images-numbered.docx")]:
        page = render_root / stem / "page-1.png"
        shutil.copyfile(page, ASSETS / f"{stem}-word.png")
        content = (source / f"{stem}.md").read_text(encoding="utf-8")
        lines = wrapped(content, 715, font(18))
        height = max(1080, 240 + len(lines) * 27)
        canvas = Image.new("RGB", (1680, height), "#eef2f8")
        draw = ImageDraw.Draw(canvas)
        draw.text((42, 28), title, font=font(34, True), fill="#14263d")
        draw.text((42, 80), "相同输入 → 本工具转换 → Microsoft Word实际导出页面", font=font(20), fill="#53657c")
        draw.rounded_rectangle((30, 125, 810, height - 30), radius=16, fill="#172a42")
        draw.rounded_rectangle((835, 125, 1650, height - 30), radius=16, fill="white")
        draw.text((55, 143), "Markdown输入", font=font(22, True), fill="#9cc6ff")
        draw.text((860, 143), "可编辑DOCX · Word页面", font=font(22, True), fill="#254a80")
        for i, line in enumerate(lines):
            draw.text((55, 190 + i * 27), line, font=font(18), fill="#e6edf7")
        rendered = fit(Image.open(page), (770, height - 235))
        canvas.paste(rendered, (857 + (770 - rendered.width) // 2, 190))
        canvas.save(ASSETS / f"{stem}-comparison.png", optimize=True)
        word = next(record for record in word_records if Path(record["document"]).stem == stem)
        records.append({"input": f"examples/showcase/{stem}.md", "inputSha256": sha(source / f"{stem}.md"), "docx": f"examples/showcase/output/{output_name}", "docxSha256": sha(source / "output" / output_name), "page": f"docs/assets/{stem}-word.png", "pageSha256": sha(page), "exportedPdfPages": len(list(page.parent.glob("page-*.png"))), "wordVersion": word["wordVersion"], "wordBuild": word["wordBuild"]})

    shutil.copyfile(WORK / "frames/05-complete.png", ASSETS / "app.png")
    frames = []
    steps = [
        ("01-empty", "1  打开工具", "选择Markdown文件，默认使用中文通用。"),
        ("02-added", "2  添加多个文件", "添加3份示例，支持一次转换多个文件。"),
        ("05-complete", "3  查看转换结果", "3份文件全部完成，可直接打开Word文档。"),
        ("work-report", "4  检查Word效果", "虚构的试运行周报，第一页包含工作进展表。"),
        ("mixed-images", "5  保留文字与图片", "项目介绍图原样嵌入，文字、单位和链接继续保留。"),
    ]
    for name, title, caption in steps:
        canvas = Image.new("RGB", (1120, 850), "#eef2f8")
        draw = ImageDraw.Draw(canvas)
        draw.text((30, 16), title, font=font(28, True), fill="#162b45")
        draw.text((30, 61), caption, font=font(18), fill="#54677e")
        path = WORK / "frames" / f"{name}.png" if name[0].isdigit() else ASSETS / f"{name}-word.png"
        image = fit(Image.open(path), (1080, 690))
        canvas.paste(image, ((1120 - image.width) // 2, 100 + (690 - image.height) // 2))
        draw.text((30, 813), "真实控件采帧与Word页面组成的步骤回放 · 已调整停留时间 · 非性能计时", font=font(16), fill="#64748b")
        frames.append(canvas.quantize(colors=192))
    frames[0].save(ASSETS / "demo.gif", save_all=True, append_images=frames[1:], duration=[3000, 4000, 4000, 7000, 6000], loop=0, disposal=2, optimize=True)

    cover = Image.new("RGB", (1280, 640), "#11243c")
    draw = ImageDraw.Draw(cover)
    draw.rectangle((0, 0, 12, 640), fill="#76aaff")
    draw.text((58, 52), "md2word", font=font(28, True), fill="#9ec4ff")
    draw.text((58, 112), "Markdown转Word", font=font(57, True), fill="white")
    draw.text((60, 206), "中文排版 · 离线批量转换 · 可编辑DOCX", font=font(27), fill="#c5d5e9")
    draw.rounded_rectangle((58, 302, 608, 510), radius=18, fill="#203b5c")
    draw.text((86, 328), "# 文档标题", font=font(29), fill="#9fc5ff")
    draw.text((86, 386), "1. 编号事项", font=font(28), fill="#edf4ff")
    draw.text((86, 438), "| 项目 | 说明 |", font=font(27), fill="#edf4ff")
    draw.text((641, 373), "→", font=font(52), fill="#8cb7ff")
    draw.rounded_rectangle((735, 294, 1218, 518), radius=18, fill="#f5f8fc")
    draw.text((774, 324), "Word文档", font=font(35, True), fill="#173454")
    draw.text((774, 387), "标题 / 自动编号 / 表格", font=font(26), fill="#355775")
    draw.text((774, 440), "保留文字、单位与本地图片", font=font(24), fill="#355775")
    draw.text((60, 567), "Windows x64  |  当前为候选状态", font=font(22), fill="#a4b8d1")
    cover.save(ASSETS / "social-preview.png", optimize=True)
    image_path = source / "assets/md2word-cover.png"
    report = {"scope": "三份固定合成样例，不代表所有输入或Word版本；首例为从零编写的虚构工作文档", "renderer": "Microsoft Word → PDF → Poppler PNG，具体版本见各样例", "visualReview": "见docs/展示验证记录.md中的人工页面检查；生成脚本不自动判定视觉通过", "uiCapture": "WinForms DrawToBitmap，实际运行并转换三份输入；非操作系统屏幕录像", "demoDurationSeconds": 24, "demoFrames": len(steps), "demoSteps": [title for _, title, _ in steps], "imageSource": {"path": "examples/showcase/assets/md2word-cover.png", "sha256": sha(image_path), "provenance": "项目所有者提供的项目介绍图；与项目分享封面相同"}, "samples": records}
    (ASSETS / "showcase.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

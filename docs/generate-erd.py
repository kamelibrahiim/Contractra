#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Contractra — مولّد مخططات ERD المرسومة
generate-erd.py  ·  الإصدار 1.0

الغرض:
    قراءة كتل ```mermaid التي تحوي erDiagram من ملف Markdown،
    ورسم كل واحدة منها ملفَّ SVG مستقلًا — ليُدمَج داخل النسخة التفاعلية.

القواعد الملزمة (ADR-012 · ADR-013):
    - Markdown هو المصدر الرسمي: المخطط يُرسم مما هو مكتوب فيه ولا شيء غيره.
    - ⛔ لا معلومة في الرسم بلا أصل في Markdown.
    - ⛔ لا CDN ولا إنترنت وقت العرض: الـSVG يُدمج داخل الملف نفسه.
    - ⛔ لا يُعدَّل SVG يدويًا — يُعاد توليده.

المتطلب الوحيد:
    Graphviz (الأمر `dot`).
    وإن لم يكن مثبَّتًا: يتوقف المولّد برسالة واضحة،
    ⛔ ولا يحذف أي SVG مولَّد سابقًا — فتبقى النسخة التفاعلية سليمة.

الاستخدام:
    python3 generate-erd.py [input.md]

المخرجات:
    erd-<n>-<hash>.svg     ·  رسم لكل مخطط
    erd-render-map.json    ·  خريطة: بصمة نص المخطط ⟸ اسم ملف الرسم
"""

import hashlib
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

DEFAULT_IN = "database-design-ar.md"
MAP_FILE = "erd-render-map.json"

# ── ألوان المجالات — نفس لوحة النسخة التفاعلية ───────────────────────────────
PALETTE = [
    ("#EEEDFE", "#3C3489"),   # بنفسجي
    ("#E1F5EE", "#14664B"),   # تركوازي
    ("#FAECE7", "#8F3A1C"),   # مرجاني
    ("#FAEEDA", "#7A4C0C"),   # كهرماني
    ("#EEF4FD", "#1F5A96"),   # أزرق
]

# ── ترجمة رموز Mermaid إلى أقدام الغراب في Graphviz ──────────────────────────
#   ||  طرف واحد إلزامي      o|  صفر أو واحد
#   |{  واحد أو أكثر          o{  صفر أو أكثر
SIDE = {
    "||": "tee",
    "o|": "teeodot",
    "|{": "crowtee",
    "o{": "crowodot",
    "}o": "crowodot",
    "}|": "crowtee",
}

REL_RE = re.compile(
    r"^\s*([A-Za-z_0-9]+)\s+([|}o][|{o])--([|o][|{]|[o|]\||[|o]\{)\s+([A-Za-z_0-9]+)\s*:"
)
ENT_RE = re.compile(r"^\s*%%\s*entity\s+([A-Za-z_0-9]+)\s*$")
DOM_RE = re.compile(r"^\s*%%\s*domain\s+(\d+)\s*:\s*(.+?)\s*$")


def find_blocks(md_text):
    """كل كتلة mermaid تحوي erDiagram — بنصها كما هو."""
    out = []
    for m in re.finditer(r"```mermaid\n(.*?)```", md_text, re.S):
        body = m.group(1)
        if "erDiagram" in body:
            out.append(body)
    return out


def parse(block):
    """يفكّك الكتلة إلى: كيانات بمجالاتها + علاقات."""
    domains, ents, rels = {}, [], []
    current = None
    for line in block.split("\n"):
        d = DOM_RE.match(line)
        if d:
            current = int(d.group(1))
            domains[current] = d.group(2)
            continue
        e = ENT_RE.match(line)
        if e:
            ents.append((e.group(1), current))
            continue
        if line.strip().startswith("%%"):
            continue
        r = REL_RE.match(line)
        if r:
            left, lsym, rsym, right = r.group(1), r.group(2), r.group(3), r.group(4)
            rels.append((left, lsym, rsym, right))
    known = {n for n, _ in ents}
    for left, _, _, right in rels:
        for n in (left, right):
            if n not in known:
                known.add(n)
                ents.append((n, None))
    return domains, ents, rels


def esc(s):
    return s.replace('"', '\\"')


def to_dot(domains, ents, rels, title=""):
    L = [
        "digraph ERD {",
        '  graph [rankdir=LR, splines=spline, overlap=false, nodesep=0.28,'
        ' ranksep=0.9, fontname="Helvetica", bgcolor="#FFFFFF", pad=0.3,'
        ' compound=true];',
        '  node [shape=box, style="rounded,filled", fontname="Helvetica",'
        ' fontsize=11, margin="0.16,0.07", penwidth=1.1];',
        '  edge [fontname="Helvetica", fontsize=9, color="#9A9A94",'
        ' penwidth=1.0, dir=both];',
    ]
    bydom = {}
    for name, dom in ents:
        bydom.setdefault(dom, []).append(name)

    for dom in sorted(bydom, key=lambda x: (x is None, x)):
        fill, stroke = PALETTE[(dom or 0) % len(PALETTE)]
        names = bydom[dom]
        if dom is None:
            for n in names:
                L.append(f'  "{esc(n)}" [fillcolor="#F1EFE8", color="#9A9A94",'
                         f' fontcolor="#2C2C2A"];')
            continue
        label = f"{dom}. {domains.get(dom, '')}".strip().rstrip(".")
        L.append(f'  subgraph "cluster_{dom}" {{')
        L.append(f'    label="{esc(label)}"; labelloc="t"; fontsize=13;'
                 f' fontcolor="{stroke}"; style="rounded,filled";'
                 f' fillcolor="{fill}80"; color="{stroke}"; penwidth=1.4;')
        for n in names:
            L.append(f'    "{esc(n)}" [fillcolor="{fill}", color="{stroke}",'
                     f' fontcolor="{stroke}"];')
        L.append("  }")

    for left, lsym, rsym, right in rels:
        tail = SIDE.get(lsym, "none")
        head = SIDE.get(rsym, "none")
        L.append(f'  "{esc(left)}" -> "{esc(right)}"'
                 f' [arrowtail="{tail}", arrowhead="{head}"];')
    L.append("}")
    return "\n".join(L)


def main():
    src = Path(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN)
    if not src.exists():
        print(f"⛔ الملف المصدر غير موجود: {src}")
        sys.exit(1)

    if shutil.which("dot") is None:
        print("⛔ Graphviz غير مثبَّت — الأمر `dot` غير موجود.")
        print("   التثبيت:  apt install graphviz   |   brew install graphviz"
              "   |   choco install graphviz")
        print("   ⚠️ لم يُحذف أي رسم مولَّد سابقًا — النسخة التفاعلية تبقى سليمة.")
        sys.exit(2)

    out_dir = src.parent
    blocks = find_blocks(src.read_text(encoding="utf-8"))
    if not blocks:
        print("⚠️ لا يوجد مخطط erDiagram في هذا الملف.")
        sys.exit(0)

    mapping = {}
    map_path = out_dir / MAP_FILE
    if map_path.exists():
        try:
            mapping = json.loads(map_path.read_text(encoding="utf-8"))
        except Exception:
            mapping = {}

    for i, block in enumerate(blocks, 1):
        digest = hashlib.sha1(block.encode("utf-8")).hexdigest()[:10]
        domains, ents, rels = parse(block)
        dot = to_dot(domains, ents, rels)
        name = f"erd-{i}-{digest}.svg"
        svg = subprocess.run(
            ["dot", "-Tsvg"], input=dot.encode("utf-8"),
            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        )
        if svg.returncode != 0:
            print(f"⛔ فشل رسم المخطط {i}: {svg.stderr.decode('utf-8')[:400]}")
            sys.exit(3)
        body = svg.stdout.decode("utf-8")
        body = body[body.index("<svg"):]                    # إسقاط ترويسة XML
        body = re.sub(r"<title>.*?</title>", "", body, flags=re.S)
        (out_dir / name).write_text(body, encoding="utf-8")
        mapping[digest] = name
        print(f"✅ المخطط {i}: {name}  ·  {len(ents)} كيانًا  ·  {len(rels)} علاقة")

    map_path.write_text(json.dumps(mapping, ensure_ascii=False, indent=1),
                        encoding="utf-8")
    print(f"✅ خريطة الرسم: {MAP_FILE}")
    print("   ⛔ لا تعدّل SVG يدويًا — أعد التوليد بعد أي تعديل على المخطط في Markdown.")


if __name__ == "__main__":
    main()

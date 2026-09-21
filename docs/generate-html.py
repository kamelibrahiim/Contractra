#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Contractra — مولّد النسخة التفاعلية
generate-html.py  ·  الإصدار 2.0  ·  تصميم Team Reference (تبويبات وبطاقات)

الغرض:
    توليد `system-specification-and-team-reference-ar.html`
    من `system-specification-and-team-reference-ar.md` — ولا شيء غيره.

القواعد الملزمة (ADR-012 · ADR-013):
    - Markdown هو المصدر الرسمي الوحيد.
    - لا معلومة في HTML بلا أصل في Markdown.
    - لا CDN ولا إنترنت ولا Framework كبير ولا Backend.
    - RTL كامل.
    - لا يُعدَّل HTML يدويًا — يُعاد توليده.

الاستخدام:
    python3 generate-html.py [input.md] [output.html]

لا اعتماديات خارجية — مكتبة Python القياسية فقط.
"""

import hashlib
import html
import json
import re
import sys
from datetime import datetime, timezone, timedelta
from pathlib import Path

CAIRO = timezone(timedelta(hours=3))

# ── المخططات المرسومة مسبقًا (generate-erd.py) ───────────────────────────────
# خريطة: بصمة نص المخطط ⟸ ملف SVG. وغيابها لا يكسر شيئًا — يُعرض المصدر النصي.
ERD_MAP_FILE = "erd-render-map.json"
_ERD_DIR = Path(".")
_ERD_MAP = None


def lookup_erd(block_text):
    """يعيد نص SVG للمخطط إن كان مرسومًا مسبقًا — وإلا None."""
    global _ERD_MAP
    if _ERD_MAP is None:
        try:
            _ERD_MAP = json.loads(
                (_ERD_DIR / ERD_MAP_FILE).read_text(encoding="utf-8"))
        except Exception:
            _ERD_MAP = {}
    digest = hashlib.sha1(block_text.encode("utf-8")).hexdigest()[:10]
    name = _ERD_MAP.get(digest)
    if not name:
        return None
    path = _ERD_DIR / name
    if not path.exists():
        return None
    return path.read_text(encoding="utf-8")

DEFAULT_IN = "system-specification-and-team-reference-ar.md"
DEFAULT_OUT = "system-specification-and-team-reference-ar.html"

# ── تجميع الأقسام الـ35 في تبويبات ───────────────────────────────────────────
# المفتاح = رقم القسم في Markdown · القيمة = التبويب الذي يقع فيه.
# هذا هو التنظيم الوحيد الذي يضيفه المولّد — ولا يضيف أي معلومة.
TABS = [
    ("overview",  "نظرة عامة",            [1, 2, 3]),
    ("scope",     "النطاق والافتراضات",    [4, 5, 6]),
    ("roles",     "الأدوار والوحدات",      [7, 8]),
    ("rules",     "قواعد العمل",           [9]),
    ("reqs",      "المتطلبات الوظيفية",    [10]),
    ("usecases",  "حالات الاستخدام",       [11, 12]),
    ("nfr",       "غير الوظيفية والأمان",  [13, 14, 15]),
    ("data",      "البيانات",              [16, 17, 18, 19]),
    ("interfaces","الواجهات والتشغيل",     [20, 21, 22, 23, 24, 25, 26, 27]),
    ("testing",   "الاختبارات والتتبع",    [28, 29, 30]),
    ("decisions", "القرارات والمخاطر",     [31, 32, 33, 34, 35]),
]

# ── تبويبات وثيقة تصميم قاعدة البيانات (16 قسمًا) ────────────────────────────
DB_TABS = [
    ("principles", "المبادئ",             [0, 1, 2]),
    ("relations",  "العلاقات والأنماط",    [3, 4, 5]),
    ("core",       "الجداول النواة",       [6]),
    ("rest",       "بقية الجداول",         [7]),
    ("matrix",     "مصفوفة العلاقات",      [8]),
    ("constraints","القيود والمحسوبات",    [9, 10, 11]),
    ("erd",        "المخطط الشامل",        [12]),
    ("notes",      "الملاحظات والقرارات",  [13, 14, 15, 16]),
]

# ── تبويبات باتش النموذج المنطقي (18 قسمًا) ──────────────────────────────────
PATCH_TABS = [
    ("summary",    "الملخص والتصحيحات", [0, 1, 2]),
    ("accounting", "المحاسبة",          [3]),
    ("treasury",   "الخزينة",           [4, 5]),
    ("money",      "المعاملة والنقد",   [6, 7, 8]),
    ("fixes",      "بقية التصحيحات",    [9, 10, 11, 12]),
    ("scope",      "النطاق والأثر",     [13, 14]),
    ("result",     "النتيجة والقرارات", [15, 16, 17]),
]

# ── تبويبات النموذج المنطقي (المجالات العشرون + الملاحق) ─────────────────────
LM_TABS = [
    ("admin",      "إدارة الوثيقة",        [0]),
    ("identity",   "الهوية والأطراف",      [101, 102]),
    ("operations", "العمليات والمواقع",    [103, 104]),
    ("spend",      "العهدة والمصروفات",    [105, 106]),
    ("payroll",    "الأجور والرواتب",      [107, 108]),
    ("suppliers",  "الموردون والمستخلصات", [109, 110]),
    ("contracts",  "الاتفاقات والمحتجزات", [111, 112]),
    ("reports",    "التقارير والملفات",    [113, 114]),
    ("audit",      "التدقيق والتنبيهات",   [115, 116]),
    ("imports",    "الاستيراد والمرجعية",  [117, 118]),
    ("accounting", "المحاسبة والخزينة",    [119, 120]),
    ("appendix",   "الملاحق",              [201, 202, 203, 204, 205, 206]),
]

# ── تبويبات باتش التصحيح v2.0.1 (18 قسمًا) ──────────────────────────────────
PATCH2_TABS = [
    ("summary",  "المشكلات والقرارات", [0, 1]),
    ("effects",  "آثار الحدث المالي",  [2, 3]),
    ("adjust",   "التصحيح والقيود",    [4, 5, 6]),
    ("naming",   "التسميات والأدوات",  [7, 8]),
    ("imports",  "الاستيراد والتنظيف", [9, 10, 11]),
    ("scope",    "الفرض والنطاق",      [12, 13, 14, 15]),
    ("result",   "النتيجة والحكم",     [16, 17, 18]),
]

TAB_SETS = {
    "system-specification-and-team-reference-ar": None,   # يُملأ بعد تعريف TABS
    "database-design-ar": DB_TABS,
    "logical-model-patch-v1.2.1-ar": PATCH_TABS,
    "logical-model-correction-patch-v2.0.1-ar": PATCH2_TABS,
    "logical-data-model-ar": LM_TABS,
}


def tabs_for(stem, sections):
    """خريطة التبويبات لهذه الوثيقة — وإلا تجميع تلقائي كل أربعة أقسام."""
    preset = TAB_SETS.get(stem)
    if preset:
        return preset
    auto, group = [], []
    for s in sections:
        group.append(s)
        if len(group) == 4:
            auto.append(group)
            group = []
    if group:
        auto.append(group)
    return [(f"t{i+1}", plain(g[0]["title"])[:28], [x["no"] for x in g])
            for i, g in enumerate(auto)]


DOTS = ["purple", "teal", "coral", "amber", "blue"]
TAB_SETS["system-specification-and-team-reference-ar"] = TABS

# ── الشارات — مشتقة من رموز Markdown نفسها ───────────────────────────────────
PILL_BADGES = [
    ("🟢", "confirmed", "مؤكدة"),
    ("🔵", "proposed", "مقترح"),
    ("🟡", "open", "قرار مفتوح"),
    ("⚪", "assumption", "افتراض"),
    ("📊", "evidence", "دليل تاريخي"),
    ("🔧", "migration", "مسألة ترحيل"),
    ("🔮", "future", "نطاق مستقبلي"),
]
INLINE_BADGES = [
    ("⛔", "forbidden", "ممنوع"),
    ("⭐", "critical", "حرج"),
    ("⚡", "idem", "منع تكرار"),
    ("🔒", "sensitive", "حساس"),
    ("⚠️", "warn", "تنبيه"),
    ("✅", "ok", "مستوفى"),
    ("❌", "no", "غير مستوفى"),
]


def slugify(text, used):
    s = re.sub(r"[`*_~⭐⛔🟢🔵🟡⚪📊🔧🔮⚡🔒⚠️✅❌]", "", text)
    s = re.sub(r"\s+", "-", s.strip())
    s = re.sub(r"[^\w؀-ۿ\-.]", "", s)
    s = s or "sec"
    base, n = s, 2
    while s in used:
        s, n = f"{base}-{n}", n + 1
    used.add(s)
    return s


def inline(text):
    out = html.escape(text, quote=False)
    out = re.sub(r"`([^`]+)`", r'<span class="mono">\1</span>', out)
    out = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", out)
    out = re.sub(r"(?<![\w*])\*([^*\n]+)\*(?![\w*])", r"<em>\1</em>", out)
    out = re.sub(r"~~([^~]+)~~", r"<del>\1</del>", out)
    out = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', out)
    for e, cls, lab in PILL_BADGES:
        out = out.replace(e, f'<span class="badge b-{cls}" title="{lab}">{e}</span>')
    for e, cls, lab in INLINE_BADGES:
        out = out.replace(e, f'<span class="mark m-{cls}" title="{lab}">{e}</span>')
    return out


def plain(text):
    t = re.sub(r"[*`~⭐⛔🟢🔵🟡⚪📊🔧🔮⚡🔒⚠️✅❌]", "", text)
    return re.sub(r"\s+", " ", t).strip()


def split_row(line):
    line = line.strip()
    if line.startswith("|"):
        line = line[1:]
    if line.endswith("|"):
        line = line[:-1]
    cells, buf, esc = [], "", False
    for ch in line:
        if esc:
            buf += ch
            esc = False
        elif ch == "\\":
            esc = True
        elif ch == "|":
            cells.append(buf.strip())
            buf = ""
        else:
            buf += ch
    cells.append(buf.strip())
    return cells


def is_sep(line):
    return bool(re.fullmatch(r"\s*\|?[\s:|-]+\|?\s*", line)) and "-" in line


def box_variant(text):
    """اختيار نوع صندوق الاقتباس من رموز Markdown نفسها."""
    if "⛔" in text:
        return "danger"
    if "⚠️" in text:
        return "warn"
    if "⭐" in text:
        return "tech"
    if "✅" in text:
        return "ok"
    return "info"


class Renderer:
    def __init__(self):
        self.used = set()
        self.dot_i = 0

    def next_dot(self):
        d = DOTS[self.dot_i % len(DOTS)]
        self.dot_i += 1
        return d

    def render(self, lines):
        """يحوّل قائمة أسطر Markdown إلى HTML بلغة التصميم."""
        out, toc = [], []
        pending_head = None          # (level, text) لآخر عنوان h4/h5 غير مستهلك
        i, n = 0, len(lines)

        def flush_head():
            nonlocal pending_head
            if pending_head:
                lvl, txt, sid = pending_head
                cls = "section-title" if lvl == 3 else "sub-title"
                out.append(f'<p class="{cls}" id="{sid}">{inline(txt)}</p>')
                pending_head = None

        while i < n:
            line = lines[i]

            # ── كتل الكود ───────────────────────────────────────
            if line.strip().startswith("```"):
                lang = line.strip()[3:].strip().lower()
                i += 1
                body = []
                while i < n and not lines[i].strip().startswith("```"):
                    body.append(lines[i])
                    i += 1
                i += 1
                code = html.escape("\n".join(body))
                title, dot, tid = self.consume_head(pending_head)
                pending_head = None
                if lang == "mermaid":
                    label, cls = "مخطط Mermaid — المصدر النصي", "diagram"
                elif lang == "gherkin":
                    label, cls = "معيار قبول — Given / When / Then", "gherkin"
                else:
                    label, cls = "مقتطف", "snippet"
                head = (
                    f'<div class="card-header"><div class="dot dot-{dot}"></div>'
                    f'<h3 class="card-title" id="{tid}">{inline(title)}</h3>'
                    f"<span>{label}</span></div>"
                    if title
                    else f'<div class="card-header"><div class="dot dot-{dot}"></div>'
                         f"<h3>{label}</h3></div>"
                )
                svg = lookup_erd("\n".join(body) + "\n") if lang == "mermaid" else None
                if svg:
                    # ⭐ مخطط مرسوم مسبقًا بـgenerate-erd.py — يُدمج داخل الملف
                    #    ⛔ بلا CDN · والمصدر النصي يبقى مطويًا تحته
                    head = head.replace(
                        f"<span>{label}</span>",
                        '<span>مخطط مرسوم — بالتكبير والتحريك</span>')
                    if "<span>" not in head:
                        head = head.replace("<h3>مخطط Mermaid — المصدر النصي</h3>",
                                            "<h3>المخطط الشامل</h3>")
                    out.append(
                        f'<div class="card code-card diagram">{head}'
                        f'<div class="erd">'
                        f'<div class="erd-bar">'
                        f'<button type="button" data-z="in">＋</button>'
                        f'<button type="button" data-z="out">−</button>'
                        f'<button type="button" data-z="fit">ملء العرض</button>'
                        f'<button type="button" data-z="all">ملء الإطار</button>'
                        f'<button type="button" data-z="reset">100%</button>'
                        f'<span class="erd-hint">اسحب للتحريك · عجلة الفأرة للتكبير</span>'
                        f'</div>'
                        f'<div class="erd-view"><div class="erd-stage">{svg}</div></div>'
                        f'</div>'
                        f'<details class="erd-src"><summary>المصدر النصي للمخطط</summary>'
                        f"<pre><code>{code}</code></pre></details></div>")
                    continue
                out.append(f'<div class="card code-card {cls}">{head}'
                           f"<pre><code>{code}</code></pre></div>")
                continue

            # ── العناوين ────────────────────────────────────────
            m = re.match(r"^(#{1,6})\s+(.*)$", line)
            if m:
                flush_head()
                lvl, txt = len(m.group(1)), m.group(2).strip()
                sid = slugify(txt, self.used)
                if lvl <= 3:
                    toc.append((lvl, sid, plain(txt)))
                if lvl <= 2:
                    out.append(f'<h2 class="panel-title" id="{sid}">{inline(txt)}</h2>')
                else:
                    pending_head = (lvl, txt, sid)
                i += 1
                continue

            # ── الفاصل ──────────────────────────────────────────
            if re.fullmatch(r"\s*(-{3,}|_{3,}|\*{3,})\s*", line):
                flush_head()
                out.append('<div class="rule-sep"></div>')
                i += 1
                continue

            # ── الجداول ─────────────────────────────────────────
            if line.strip().startswith("|") and i + 1 < n and is_sep(lines[i + 1]):
                head_cells = split_row(line)
                i += 2
                rows = []
                while i < n and lines[i].strip().startswith("|"):
                    rows.append(split_row(lines[i]))
                    i += 1
                title, dot, tid = self.consume_head(pending_head)
                pending_head = None
                t = ['<div class="card">']
                if title:
                    t.append(f'<div class="card-header"><div class="dot dot-{dot}"></div>'
                             f'<h3 class="card-title" id="{tid}">{inline(title)}</h3>'
                             f"<span>{len(rows)} صفًّا</span></div>")
                t.append('<div class="tbl-wrap"><table><thead><tr>')
                t += [f"<th>{inline(c)}</th>" for c in head_cells]
                t.append("</tr></thead><tbody>")
                for r in rows:
                    r += [""] * (len(head_cells) - len(r))
                    t.append("<tr>" + "".join(f"<td>{inline(c)}</td>"
                                              for c in r[: len(head_cells)]) + "</tr>")
                t.append("</tbody></table></div></div>")
                out.append("".join(t))
                continue

            # ── الاقتباس ────────────────────────────────────────
            if line.strip().startswith(">"):
                flush_head()
                buf = []
                while i < n and lines[i].strip().startswith(">"):
                    buf.append(re.sub(r"^\s*>\s?", "", lines[i]))
                    i += 1
                raw = "\n".join(buf)
                var = box_variant(raw)
                inner = self.render_simple(raw.split("\n"))
                out.append(f'<div class="info-box {var}">{inner}</div>')
                continue

            # ── القوائم ─────────────────────────────────────────
            if re.match(r"^\s*([-*+]|\d+\.)\s+", line):
                ordered = bool(re.match(r"^\s*\d+\.\s+", line))
                items = []
                while i < n and re.match(r"^\s*([-*+]|\d+\.)\s+", lines[i]):
                    items.append(re.sub(r"^\s*([-*+]|\d+\.)\s+", "", lines[i]))
                    i += 1
                title, dot, tid = self.consume_head(pending_head)
                pending_head = None
                tag = "ol" if ordered else "ul"
                body = f'<{tag} class="rule-list">' + "".join(
                    f"<li>{inline(x)}</li>" for x in items) + f"</{tag}>"
                if title:
                    out.append(f'<div class="card"><div class="card-header">'
                               f'<div class="dot dot-{dot}"></div>'
                               f'<h3 class="card-title" id="{tid}">{inline(title)}</h3>'
                               f"</div>{body}</div>")
                else:
                    out.append(f'<div class="card bare">{body}</div>')
                continue

            # ── الفقرات ─────────────────────────────────────────
            if line.strip() == "":
                i += 1
                continue
            buf = []
            while i < n and lines[i].strip() != "" and not re.match(
                r"^(#{1,6}\s|\s*\||\s*>|\s*([-*+]|\d+\.)\s|```|\s*(-{3,}|_{3,}|\*{3,})\s*$)",
                lines[i],
            ):
                buf.append(lines[i])
                i += 1
            if buf:
                flush_head()
                out.append('<p class="para">' + inline(" ".join(buf)) + "</p>")

        flush_head()
        return "\n".join(out), toc

    def consume_head(self, pending):
        """يحوّل عنوانًا معلّقًا إلى ترويسة بطاقة (مع مُعرِّف للربط والفهرس)."""
        if not pending:
            return None, self.next_dot(), None
        _, txt, sid = pending
        return txt, self.next_dot(), sid

    def render_simple(self, lines):
        """عرض مبسّط داخل الصناديق — بلا بطاقات متداخلة."""
        out, i, n = [], 0, len(lines)
        while i < n:
            line = lines[i]
            if line.strip().startswith("|") and i + 1 < n and is_sep(lines[i + 1]):
                head = split_row(line)
                i += 2
                rows = []
                while i < n and lines[i].strip().startswith("|"):
                    rows.append(split_row(lines[i]))
                    i += 1
                t = ['<div class="tbl-wrap"><table><thead><tr>']
                t += [f"<th>{inline(c)}</th>" for c in head]
                t.append("</tr></thead><tbody>")
                for r in rows:
                    r += [""] * (len(head) - len(r))
                    t.append("<tr>" + "".join(f"<td>{inline(c)}</td>"
                                              for c in r[: len(head)]) + "</tr>")
                t.append("</tbody></table></div>")
                out.append("".join(t))
                continue
            if re.match(r"^\s*([-*+]|\d+\.)\s+", line):
                items = []
                while i < n and re.match(r"^\s*([-*+]|\d+\.)\s+", lines[i]):
                    items.append(re.sub(r"^\s*([-*+]|\d+\.)\s+", "", lines[i]))
                    i += 1
                out.append('<ul class="rule-list tight">' +
                           "".join(f"<li>{inline(x)}</li>" for x in items) + "</ul>")
                continue
            m = re.match(r"^\s*(#{1,6})\s+(.*)$", line)
            if m:
                out.append(f'<p class="box-title">{inline(m.group(2).strip())}</p>')
                i += 1
                continue
            if line.strip() == "":
                i += 1
                continue
            buf = []
            while i < n and lines[i].strip() != "" and not re.match(
                r"^(\s*\||\s*([-*+]|\d+\.)\s|\s*#{1,6}\s)", lines[i]
            ):
                buf.append(lines[i])
                i += 1
            if buf:
                out.append("<p>" + inline(" ".join(buf)) + "</p>")
        return "\n".join(out)


def split_sections(md):
    """يقسّم Markdown إلى أقسام — ويدعم ثلاثة أنماط عناوين:

        `## N. عنوان`        ⟸ رقم القسم = N
        `# Domain N — عنوان` ⟸ رقم القسم = 100 + N
        `# عنوان` آخر (ملحق) ⟸ 200 فصاعدًا بترتيب الظهور

    ⛔ لا يضيف معلومة — يقرأ ما في Markdown فقط.
    """
    lines = md.split("\n")
    head_lines, sections, cur = [], [], None
    extra = 200
    for ln in lines:
        no = title = None
        m = re.match(r"^##\s+(\d+)\.\s+(.*)$", ln)
        if m:
            no, title = int(m.group(1)), m.group(2).strip()
        else:
            m = re.match(r"^#\s+Domain\s+(\d+)\s*[—–-]\s*(.*)$", ln)
            if m:
                no, title = 100 + int(m.group(1)), f"Domain {m.group(1)} — {m.group(2).strip()}"
            elif re.match(r"^#\s+(ملحق|Appendix)\b", ln):
                extra += 1
                no, title = extra, ln[2:].strip()
        if no is not None:
            if cur:
                sections.append(cur)
            cur = {"no": no, "title": title, "lines": [ln]}
        elif cur is not None:
            cur["lines"].append(ln)
        else:
            head_lines.append(ln)
    if cur:
        sections.append(cur)

    # ⭐ احتياطي: وثيقة بلا عناوين مرقّمة (مثل سجل القرارات `## ADR-001 — …`)
    #    تُقسَّم على كل عنوان `##` بترقيم تسلسلي — ⛔ لئلا تصبح لوحة واحدة ضخمة.
    if len(sections) <= 1:
        head_lines, sections, cur, i = [], [], None, 0
        for ln in lines:
            if re.match(r"^##\s+\S", ln):
                if cur:
                    sections.append(cur)
                i += 1
                cur = {"no": i, "title": ln[3:].strip(), "lines": [ln]}
            elif cur is not None:
                cur["lines"].append(ln)
            else:
                head_lines.append(ln)
        if cur:
            sections.append(cur)
    return head_lines, sections


CSS = r"""
:root{
  --purple:#7F77DD; --purple-dk:#3C3489; --purple-lt:#EEEDFE;
  --teal:#1D9E75;   --teal-lt:#E1F5EE;
  --coral:#D85A30;  --coral-lt:#FAECE7;
  --amber:#BA7517;  --amber-lt:#FAEEDA;
  --blue:#378ADD;   --blue-lt:#EEF4FD;
  --gray-dk:#2C2C2A; --gray-md:#6B6B68; --gray-lt:#F1EFE8;
  --gray-bd:#D3D1C7; --white:#fff; --bg:#F8F7F4;
  --font-sans:"Segoe UI","Noto Naskh Arabic","Tahoma",system-ui,-apple-system,sans-serif;
  --font-mono:"Cascadia Mono","SF Mono","Consolas","Courier New",monospace;
  --radius:8px; --radius-lg:12px;
}
*{box-sizing:border-box;margin:0;padding:0}
html{scroll-behavior:smooth}
body{
  font-family:var(--font-sans); background:var(--bg); color:var(--gray-dk);
  font-size:14px; line-height:1.7; direction:rtl; text-align:right;
}

/* ── الترويسة ─────────────────────────────────────── */
.site-header{
  background:var(--purple-dk); padding:26px 40px; position:sticky; top:0; z-index:100;
  border-bottom:1px solid rgba(255,255,255,.1);
  display:flex; align-items:center; gap:20px; flex-wrap:wrap;
}
.site-header .ttl{flex:1; min-width:240px}
.site-header h1{font-size:21px; font-weight:700; color:#fff; letter-spacing:-.2px}
.site-header p{font-size:12.5px; color:rgba(255,255,255,.55); margin-top:3px}
.hdr-meta{display:flex; gap:8px; flex-wrap:wrap; align-items:center}
.chip-v{
  font-size:11px; font-weight:700; padding:4px 11px; border-radius:20px;
  background:rgba(255,255,255,.14); color:#fff; border:1px solid rgba(255,255,255,.22);
}
#q{
  width:220px; padding:7px 12px; border-radius:20px; font-family:inherit; font-size:12.5px;
  border:1px solid rgba(255,255,255,.22); background:rgba(255,255,255,.1); color:#fff;
}
#q::placeholder{color:rgba(255,255,255,.5)}
#q:focus{outline:none; background:rgba(255,255,255,.18); border-color:rgba(255,255,255,.45)}

/* ── التبويبات ────────────────────────────────────── */
.nav{
  background:var(--white); border-bottom:1px solid var(--gray-bd);
  padding:0 40px; display:flex; flex-wrap:wrap; gap:0 2px;
  position:sticky; top:81px; z-index:99;   /* يُضبط من JS حسب ارتفاع الترويسة */
}
.nav-btn{
  font-size:13px; font-weight:600; padding:12px 15px; border:none; background:none;
  color:var(--gray-md); cursor:pointer; border-bottom:2px solid transparent;
  white-space:nowrap; transition:all .15s; font-family:inherit;
}
.nav-btn:hover{color:var(--gray-dk)}
.nav-btn.active{color:var(--purple); border-bottom-color:var(--purple)}
.nav-btn .cnt{font-size:10px; color:var(--gray-bd); margin-inline-start:5px; font-weight:700}
.nav-btn.active .cnt{color:var(--purple)}

/* ── المخطط المرسوم ───────────────────────────────── */
.erd{border-bottom:1px solid var(--gray-bd); background:#fbfbf9}
.erd-bar{
  display:flex; align-items:center; gap:6px; flex-wrap:wrap;
  padding:8px 14px; border-bottom:1px solid var(--gray-bd); background:var(--white);
}
.erd-bar button{
  font-family:inherit; font-size:12px; font-weight:700; cursor:pointer;
  padding:5px 11px; border-radius:7px; border:1px solid var(--gray-bd);
  background:var(--white); color:var(--gray-dk); line-height:1.2;
}
.erd-bar button:hover{background:var(--purple-lt); border-color:var(--purple); color:var(--purple-dk)}
.erd-hint{font-size:11.5px; color:var(--gray-md); margin-inline-start:auto}
.erd-view{
  height:min(74vh,720px); overflow:hidden; position:relative;
  cursor:grab; background:
    linear-gradient(90deg,rgba(0,0,0,.028) 1px,transparent 1px) 0 0/22px 22px,
    linear-gradient(rgba(0,0,0,.028) 1px,transparent 1px) 0 0/22px 22px, #fff;
}
.erd-view.drag{cursor:grabbing}
.erd-stage{transform-origin:0 0; will-change:transform; direction:ltr}
.erd-stage svg{display:block; max-width:none; height:auto}
.erd-src{border-top:1px solid var(--gray-bd)}
.erd-src summary{
  cursor:pointer; padding:9px 16px; font-size:12.5px; font-weight:700;
  color:var(--purple-dk); background:#fcfcfa; list-style:none;
}
.erd-src summary::-webkit-details-marker{display:none}
.erd-src summary::before{content:"▸ "; font-size:11px}
.erd-src[open] summary::before{content:"▾ "}
.erd-src pre{border-top:1px solid var(--gray-bd)}

/* ── المحتوى ──────────────────────────────────────── */
.content{max-width:1180px; margin:0 auto; padding:30px 40px 80px}
.panel{display:none}
.panel.active{display:block}

.panel-title{
  font-size:19px; font-weight:700; color:var(--gray-dk);
  margin:34px 0 4px; padding-bottom:9px; border-bottom:2px solid var(--purple);
  scroll-margin-top:var(--stick,150px);
}
.panel > .panel-title:first-child{margin-top:0}
.section-title{
  font-size:14.5px; font-weight:700; color:var(--purple-dk);
  margin:26px 0 10px; padding-inline-start:10px;
  border-inline-start:3px solid var(--purple); scroll-margin-top:var(--stick,150px);
}
.sub-title{
  font-size:13px; font-weight:700; color:var(--gray-dk);
  margin:18px 0 8px; scroll-margin-top:var(--stick,150px);
}
.para{font-size:13.5px; color:var(--gray-dk); margin:10px 2px}
.rule-sep{height:1px; background:var(--gray-bd); margin:26px 0; opacity:.6}

/* ── البطاقات ─────────────────────────────────────── */
.card{
  background:var(--white); border:1px solid var(--gray-bd);
  border-radius:var(--radius-lg); overflow:hidden; margin-bottom:12px;
}
.card.bare{border-style:solid}
.card-header{
  display:flex; align-items:center; gap:10px; padding:11px 16px;
  border-bottom:1px solid var(--gray-bd); background:#fcfcfa;
}
.card-header h3{font-size:13.5px; font-weight:700; color:var(--gray-dk); line-height:1.5}
.card-title{scroll-margin-top:var(--stick,150px)}
.card-header span{
  font-size:10.5px; color:var(--gray-md); margin-inline-start:auto;
  white-space:nowrap; flex-shrink:0;
}
.dot{width:9px; height:9px; border-radius:50%; flex-shrink:0}
.dot-purple{background:var(--purple)} .dot-teal{background:var(--teal)}
.dot-coral{background:var(--coral)}  .dot-amber{background:var(--amber)}
.dot-blue{background:var(--blue)}

/* ── الجداول ──────────────────────────────────────── */
.tbl-wrap{overflow-x:auto}
table{width:100%; border-collapse:collapse; font-size:12.8px}
th{
  text-align:right; font-size:10.5px; font-weight:700; text-transform:uppercase;
  letter-spacing:.05em; color:var(--gray-md); padding:9px 13px;
  background:var(--gray-lt); border-bottom:1px solid var(--gray-bd);
  position:sticky; top:0; z-index:1; white-space:nowrap;
}
td{padding:9px 13px; border-bottom:1px solid var(--gray-bd); vertical-align:top; line-height:1.65}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover td{background:var(--gray-lt)}
td strong{font-weight:700}

/* ── القوائم ──────────────────────────────────────── */
.rule-list{list-style:none; padding:14px 16px; display:flex; flex-direction:column; gap:7px}
.rule-list.tight{padding:6px 0}
.rule-list li{
  display:flex; gap:9px; align-items:flex-start;
  font-size:13px; color:var(--gray-dk); line-height:1.65;
}
.rule-list li::before{content:'—'; color:var(--gray-bd); flex-shrink:0; margin-top:1px}
ol.rule-list{counter-reset:ri}
ol.rule-list li{counter-increment:ri}
ol.rule-list li::before{
  content:counter(ri); color:var(--purple); font-weight:700; font-size:11px;
  min-width:17px; text-align:center;
}

/* ── الصناديق ─────────────────────────────────────── */
.info-box{
  border-inline-start:3px solid var(--blue); background:var(--blue-lt);
  border-radius:0 var(--radius) var(--radius) 0;
  padding:11px 15px; font-size:13px; color:var(--gray-dk); margin:12px 0;
}
.info-box.warn{border-inline-start-color:var(--amber); background:var(--amber-lt)}
.info-box.danger{border-inline-start-color:var(--coral); background:var(--coral-lt)}
.info-box.tech{border-inline-start-color:var(--purple); background:var(--purple-lt)}
.info-box.ok{border-inline-start-color:var(--teal); background:var(--teal-lt)}
.info-box p{margin:5px 0}
.info-box p:first-child{margin-top:0}
.info-box p:last-child{margin-bottom:0}
.info-box .box-title{font-size:13.5px; font-weight:700; margin-bottom:6px}
.info-box table{font-size:12.3px; margin-top:8px}
.info-box th{background:rgba(255,255,255,.55)}
.info-box td{border-bottom-color:rgba(0,0,0,.07)}
.info-box tbody tr:hover td{background:rgba(255,255,255,.4)}

/* ── الكود ────────────────────────────────────────── */
.code-card pre{
  margin:0; padding:14px 16px; overflow-x:auto; direction:ltr; text-align:left;
  font-family:var(--font-mono); font-size:12px; line-height:1.7;
  background:#fbfaf7; color:var(--gray-dk);
}
.code-card.gherkin pre{background:var(--teal-lt)}
.code-card.diagram pre{background:var(--gray-lt); max-height:420px}
.mono{
  font-family:var(--font-mono); font-size:.9em; direction:ltr;
  unicode-bidi:isolate; display:inline-block; background:var(--gray-lt);
  padding:.1em .38em; border-radius:4px;
}
th .mono, .card-header .mono{background:transparent; padding:0}

/* ── الشارات ──────────────────────────────────────── */
.badge{
  display:inline-block; font-size:11px; line-height:1.5; padding:1px 7px;
  border-radius:20px; font-weight:600; vertical-align:middle;
}
.b-confirmed{background:var(--teal-lt)}
.b-proposed{background:var(--blue-lt)}
.b-open{background:var(--amber-lt)}
.b-assumption{background:var(--gray-lt)}
.b-evidence{background:var(--purple-lt)}
.b-migration{background:var(--coral-lt)}
.b-future{background:var(--gray-lt)}
.mark{font-style:normal}
.m-forbidden,.m-no{color:var(--coral)}
.m-critical{color:var(--amber)}
.m-ok{color:var(--teal)}
.m-warn{color:var(--amber)}
.m-idem{color:var(--purple)}
.m-sensitive{color:var(--coral)}
del{color:var(--gray-md)}
a{color:var(--purple-dk)}
mark{background:#FFE9A8; color:var(--gray-dk); border-radius:3px; padding:0 2px}

/* ── فهرس جانبي عائم ──────────────────────────────── */
.mini-toc{
  position:fixed; inset-inline-start:14px; bottom:60px; z-index:60;
  background:var(--white); border:1px solid var(--gray-bd); border-radius:var(--radius-lg);
  max-width:280px; max-height:min(66vh,560px); overflow-y:auto; padding:9px 6px;
  box-shadow:0 4px 18px rgba(0,0,0,.09); display:none;
}
.mini-toc.open{display:block}
.mini-toc a{
  display:block; padding:4px 9px; border-radius:6px; font-size:11.8px;
  color:var(--gray-dk); text-decoration:none; line-height:1.5;
}
.mini-toc a:hover{background:var(--gray-lt)}
.mini-toc a.l3{padding-inline-start:20px; font-size:11.2px; color:var(--gray-md)}
.mini-toc a.hide{display:none}

/* ── أزرار عائمة ──────────────────────────────────── */
.fab{position:fixed; inset-inline-start:14px; bottom:14px; z-index:70; display:flex; gap:6px}
.fab.up{inset-inline-start:auto; inset-inline-end:14px}
.fab button{
  border:1px solid var(--gray-bd); background:var(--white); color:var(--gray-dk);
  border-radius:20px; padding:7px 13px; cursor:pointer; font-family:inherit;
  font-size:12px; font-weight:600; box-shadow:0 2px 8px rgba(0,0,0,.07);
}
.fab button:hover{background:var(--gray-lt)}
.hits{
  font-size:11px; color:rgba(255,255,255,.7); white-space:nowrap;
}

/* ── الاستجابة ────────────────────────────────────── */
@media (max-width:820px){
  .site-header{padding:16px; position:static}
  .nav{padding:0 12px; position:static}
  .content{padding:20px 14px 70px}
  #q{width:100%}
  .mini-toc{max-width:min(250px,84vw)}
}

/* ── الطباعة ──────────────────────────────────────── */
@media print{
  .nav,.fab,.mini-toc,#q,.hits,.erd-bar{display:none !important}
  .erd-view{height:auto; overflow:visible}
  .erd-src{display:none}
  .site-header{position:static; background:#fff; border-bottom:2px solid #000; padding:0 0 10px}
  .site-header h1,.site-header p{color:#000}
  .chip-v{background:#eee; color:#000; border-color:#999}
  body{background:#fff; font-size:10pt}
  .content{max-width:100%; padding:0}
  .panel{display:block !important}
  .panel-title{page-break-before:always; page-break-after:avoid}
  .panel:first-child .panel-title:first-child{page-break-before:avoid}
  .card,.info-box,.code-card{page-break-inside:avoid; box-shadow:none}
  table{font-size:8pt}
  tr{page-break-inside:avoid}
  th{position:static; -webkit-print-color-adjust:exact; print-color-adjust:exact}
  .badge,.dot{-webkit-print-color-adjust:exact; print-color-adjust:exact}
  a{color:#000; text-decoration:none}
  @page{size:A4; margin:14mm 12mm}
}
"""

JS = r"""
(function(){
  var q=document.getElementById('q'), hits=document.getElementById('hits');
  var toc=document.getElementById('toc'), links;

  /* ارتفاع الترويسة والتبويبات متغيّر (سطر أو سطران) — نقيسه بدل تثبيته */
  var hdrEl=document.querySelector('.site-header'), navEl=document.querySelector('.nav');
  function syncSticky(){
    var h=hdrEl.offsetHeight;
    navEl.style.top=h+'px';
    document.documentElement.style.setProperty('--stick',(h+navEl.offsetHeight+14)+'px');
  }
  syncSticky();
  window.addEventListener('resize',syncSticky);
  if(window.ResizeObserver){ new ResizeObserver(syncSticky).observe(hdrEl); }

  function show(id,btn){
    document.querySelectorAll('.panel').forEach(function(p){p.classList.remove('active')});
    document.querySelectorAll('.nav-btn').forEach(function(b){b.classList.remove('active')});
    var el=document.getElementById(id); if(el) el.classList.add('active');
    if(btn) btn.classList.add('active');
    buildToc(id);
    window.scrollTo({top:0,behavior:'instant'});
    history.replaceState(null,'','#'+id);
  }
  window.__show=show;
  document.querySelectorAll('.nav-btn').forEach(function(b){
    b.addEventListener('click',function(){show(b.dataset.tab,b)});
  });

  function buildToc(id){
    var p=document.getElementById(id); if(!p) return;
    var hs=p.querySelectorAll('.panel-title,.section-title,.card-title');
    var h='';
    hs.forEach(function(x){
      var cls=x.classList.contains('panel-title')?'l2':'l3';
      h+='<a class="'+cls+'" href="#'+x.id+'">'+x.textContent.replace(/#$/,'')+'</a>';
    });
    toc.innerHTML=h;
    links=Array.prototype.slice.call(toc.querySelectorAll('a'));
  }

  document.getElementById('tocBtn').addEventListener('click',function(){
    toc.classList.toggle('open');
  });
  document.getElementById('upBtn').addEventListener('click',function(){
    window.scrollTo({top:0,behavior:'smooth'});
  });

  // ── تكبير وتحريك المخططات المرسومة ──────────────────
  document.querySelectorAll('.erd').forEach(function(box){
    var view=box.querySelector('.erd-view'), stage=box.querySelector('.erd-stage');
    var svg=stage.querySelector('svg'); if(!svg) return;
    var w=parseFloat(svg.getAttribute('width'))||svg.getBoundingClientRect().width;
    var h=parseFloat(svg.getAttribute('height'))||svg.getBoundingClientRect().height;
    svg.removeAttribute('width'); svg.removeAttribute('height');
    svg.style.width=w+'px'; svg.style.height=h+'px';
    var s=1,x=0,y=0;
    function apply(){stage.style.transform='translate('+x+'px,'+y+'px) scale('+s+')'}
    function fit(){            // ملء العرض: العرض كاملًا والتمرير رأسيًا بالسحب
      s=Math.max(0.05,Math.min(view.clientWidth/w,1));
      x=(view.clientWidth-w*s)/2; y=10; apply();
    }
    function fitAll(){         // ملء الإطار: المخطط كله داخل الإطار
      var k=Math.min(view.clientWidth/w, view.clientHeight/h);
      s=Math.max(0.03,Math.min(k,1)); x=(view.clientWidth-w*s)/2; y=6; apply();
    }
    function zoom(f,cx,cy){
      var ns=Math.min(4,Math.max(0.05,s*f));
      x=cx-(cx-x)*(ns/s); y=cy-(cy-y)*(ns/s); s=ns; apply();
    }
    box.querySelectorAll('.erd-bar button').forEach(function(b){
      b.addEventListener('click',function(){
        var k=b.dataset.z;
        if(k==='in')  zoom(1.25,view.clientWidth/2,view.clientHeight/2);
        if(k==='out') zoom(0.8, view.clientWidth/2,view.clientHeight/2);
        if(k==='fit') fit();
        if(k==='all') fitAll();
        if(k==='reset'){s=1;x=0;y=0;apply()}
      });
    });
    view.addEventListener('wheel',function(e){
      e.preventDefault();
      var r=view.getBoundingClientRect();
      zoom(e.deltaY<0?1.12:0.89, e.clientX-r.left, e.clientY-r.top);
    },{passive:false});
    var dx=0,dy=0,down=false;
    view.addEventListener('pointerdown',function(e){
      down=true; dx=e.clientX-x; dy=e.clientY-y;
      view.classList.add('drag'); view.setPointerCapture(e.pointerId);
    });
    view.addEventListener('pointermove',function(e){
      if(!down) return; x=e.clientX-dx; y=e.clientY-dy; apply();
    });
    ['pointerup','pointercancel'].forEach(function(ev){
      view.addEventListener(ev,function(){down=false; view.classList.remove('drag')});
    });
    // اللوحة قد تكون مخفية عند التحميل — فالضبط يتم أول ما تظهر بمقاس حقيقي
    var fitted=false;
    function tryFit(){
      if(fitted || view.clientWidth<40 || view.clientHeight<40) return;
      fitted=true; fit();
    }
    tryFit();
    if(window.ResizeObserver){ new ResizeObserver(tryFit).observe(view); }
    window.addEventListener('resize',function(){ if(fitted && s<=0.25) fit(); });
  });

  // بحث عبر كل التبويبات
  function clearMarks(){
    Array.prototype.slice.call(document.querySelectorAll('mark')).forEach(function(m){
      m.parentNode.replaceChild(document.createTextNode(m.textContent),m);
    });
    document.querySelectorAll('.panel').forEach(function(p){p.normalize()});
  }
  var timer;
  q.addEventListener('input',function(){
    clearTimeout(timer); timer=setTimeout(runSearch,260);
  });
  function runSearch(){
    clearMarks();
    var v=q.value.trim();
    document.querySelectorAll('.nav-btn .cnt').forEach(function(c){c.textContent=c.dataset.n});
    if(v.length<2){ hits.textContent=''; return; }
    var total=0;
    document.querySelectorAll('.panel').forEach(function(p){
      var c=0;
      var w=document.createTreeWalker(p,NodeFilter.SHOW_TEXT,null), nodes=[], nd;
      while((nd=w.nextNode())){ if(nd.nodeValue.indexOf(v)!==-1) nodes.push(nd); }
      nodes.forEach(function(node){
        var parts=node.nodeValue.split(v), frag=document.createDocumentFragment();
        for(var i=0;i<parts.length;i++){
          frag.appendChild(document.createTextNode(parts[i]));
          if(i<parts.length-1){ var m=document.createElement('mark'); m.textContent=v; frag.appendChild(m); c++; }
        }
        node.parentNode.replaceChild(frag,node);
      });
      total+=c;
      var btn=document.querySelector('.nav-btn[data-tab="'+p.id+'"] .cnt');
      if(btn) btn.textContent=c?c:'0';
    });
    hits.textContent = total ? ('مطابقات: '+total) : 'لا مطابقات';
    var first=document.querySelector('.panel.active mark');
    if(first) first.scrollIntoView({block:'center'});
  }

  // فتح التبويب من الرابط
  var h=location.hash.slice(1);
  var start=document.querySelector('.nav-btn[data-tab="'+h+'"]');
  if(start) show(h,start); else buildToc(document.querySelector('.panel.active').id);

  // روابط الفهرس تغلقه بعد القفز
  document.addEventListener('click',function(e){
    if(e.target.closest('#toc a')) toc.classList.remove('open');
  });
})();
"""


def build(md_path: Path, out_path: Path):
    md = md_path.read_text(encoding="utf-8")
    head_lines, sections = split_sections(md)

    ver = "—"
    m = re.search(r"\*\*الإصدار\*\*\s*\|\s*\*\*([0-9.]+)\*\*", md)
    if m:
        ver = m.group(1)

    # عنوانا الوثيقة من سطري `#` الأولين — ⛔ لا عنوان مكتوب في المولّد
    h1s = [plain(x[2:]) for x in md.split("\n") if x.startswith("# ")][:2]
    doc_ar = h1s[0] if h1s else "Contractra"
    doc_en = h1s[1] if len(h1s) > 1 else ""
    if " — " in doc_ar:
        brand, doc_ar = doc_ar.split(" — ", 1)
    else:
        brand = "Contractra"
    sub = doc_ar + (" &nbsp;·&nbsp; " + html.escape(doc_en) if doc_en else "")

    by_no = {s["no"]: s for s in sections}
    used_no, panels, navs = set(), [], []
    tab_map = tabs_for(md_path.stem, sections)

    for key, label, nums in tab_map:
        r = Renderer()
        chunks, cnt = [], 0
        for no in nums:
            s = by_no.get(no)
            if not s:
                continue
            used_no.add(no)
            body, _ = r.render(s["lines"])
            chunks.append(body)
            cnt += 1
        if not chunks:
            continue
        panels.append(f'<div id="{key}" class="panel">' + "\n".join(chunks) + "</div>")
        navs.append(
            f'<button class="nav-btn" data-tab="{key}">{html.escape(label)}'
            f'<span class="cnt" data-n="{cnt}">{cnt}</span></button>'
        )

    # أي قسم لم يدخل تبويبًا يُلحق بآخر تبويب — لئلا تضيع معلومة
    orphans = [s for s in sections if s["no"] not in used_no]
    if orphans:
        r = Renderer()
        extra = "\n".join(r.render(s["lines"])[0] for s in orphans)
        panels[-1] = panels[-1][:-6] + extra + "</div>"

    panels[0] = panels[0].replace('class="panel"', 'class="panel active"', 1)
    navs[0] = navs[0].replace('class="nav-btn"', 'class="nav-btn active"', 1)

    gen = datetime.now(CAIRO).strftime("%Y-%m-%d · %H:%M")
    doc = f"""<!DOCTYPE html>
<html lang="ar" dir="rtl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(brand)} — {html.escape(doc_ar)}</title>
<meta name="description" content="النسخة التفاعلية المولَّدة من {html.escape(doc_ar)} — Markdown هو المصدر الرسمي.">
<meta name="generator" content="generate-html.py v2.0">
<style>{CSS}</style>
</head>
<body>

<header class="site-header">
  <div class="ttl">
    <h1>{html.escape(brand)} &nbsp;·&nbsp; كونتراكترا</h1>
    <p>{sub}</p>
  </div>
  <div class="hdr-meta">
    <span class="chip-v">الإصدار {html.escape(ver)}</span>
    <span class="chip-v">توليد {gen}</span>
    <input id="q" type="search" placeholder="ابحث في كل التبويبات…" autocomplete="off">
    <span class="hits" id="hits"></span>
  </div>
</header>

<nav class="nav">{''.join(navs)}</nav>

<div class="content">
{''.join(panels)}
</div>

<div class="mini-toc" id="toc"></div>
<div class="fab"><button id="tocBtn" type="button">الفهرس</button></div>
<div class="fab up"><button id="upBtn" type="button">أعلى</button></div>

<script>{JS}</script>
</body>
</html>
"""
    out_path.write_text(doc, encoding="utf-8")
    return len(sections), len(panels), len(doc)


def main():
    base = Path(__file__).resolve().parent
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else base / DEFAULT_IN
    dst = Path(sys.argv[2]) if len(sys.argv) > 2 else base / DEFAULT_OUT
    if not src.exists():
        print(f"⛔ الملف المصدر غير موجود: {src}")
        sys.exit(1)
    global _ERD_DIR
    _ERD_DIR = src.resolve().parent          # مكان ملفات المخططات المرسومة
    n_sec, n_tab, size = build(src, dst)
    print(f"✅ تم التوليد: {dst.name}")
    print(f"   الأقسام: {n_sec}   ·   التبويبات: {n_tab}")
    print(f"   حجم الملف: {size:,} حرفًا")
    print("   ⛔ لا تعدّل HTML يدويًا — أعد التوليد بعد أي تعديل على Markdown.")


if __name__ == "__main__":
    main()

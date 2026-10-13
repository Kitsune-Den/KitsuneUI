"""Apply KitsuneUI's XML patches to the vanilla files offline and report any that miss.

A dedicated server only patches what it loads, and it never loads the main-menu
UI (XUi_Menu), so a menu patch that matches nothing would only show up in a
client's log. This applies each patch file to its vanilla counterpart with lxml,
in the game's order (top to bottom), and lists every patch that matched no node.

    python tools/check_patches.py "<7 Days To Die>/Data/Config" [KitsuneUI/Config]

Handles set, setattribute, append, prepend, insertBefore, insertAfter, remove,
removeattribute. <conditional> blocks are reported as skipped.
"""

import copy
import sys
from pathlib import Path

from lxml import etree

ROOT = Path(__file__).resolve().parent.parent


def apply(target_root, patch_root, label):
    misses, applied, skipped = [], 0, 0
    for op in patch_root:
        if not isinstance(op.tag, str):
            continue  # comments
        if op.tag == "conditional":
            skipped += 1
            continue
        xp = op.get("xpath")
        try:
            nodes = target_root.xpath(xp)
        except etree.XPathError as e:
            misses.append(f"{label}: BAD XPATH {xp} ({e})")
            continue
        if not nodes:
            misses.append(f"{label}: no match  <{op.tag} xpath=\"{xp}\">")
            continue
        applied += 1
        for n in nodes:
            if op.tag == "set":
                if isinstance(n, etree._ElementUnicodeResult) and n.is_attribute:
                    n.getparent().set(n.attrname, op.text or "")
                else:
                    n.text = op.text
            elif op.tag == "setattribute":
                n.set(op.get("name"), op.text or "")
            elif op.tag == "removeattribute":
                n.attrib.pop(op.get("name"), None)
            elif op.tag == "remove":
                if isinstance(n, etree._ElementUnicodeResult):
                    n.getparent().attrib.pop(n.attrname, None)
                else:
                    n.getparent().remove(n)
            elif op.tag in ("append", "prepend", "insertBefore", "insertAfter"):
                kids = [copy.deepcopy(k) for k in op if isinstance(k.tag, str)]
                if op.tag == "append":
                    for k in kids:
                        n.append(k)
                elif op.tag == "prepend":
                    for k in reversed(kids):
                        n.insert(0, k)
                elif op.tag == "insertBefore":
                    for k in kids:
                        n.addprevious(k)
                else:
                    for k in reversed(kids):
                        n.addnext(k)
    return applied, misses, skipped


def main():
    vanilla = Path(sys.argv[1])
    mod = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / "KitsuneUI" / "Config"
    parser = etree.XMLParser(remove_comments=False)
    total_misses = []
    for patch_file in sorted(mod.rglob("*.xml")):
        rel = patch_file.relative_to(mod)
        target = vanilla / rel
        if not target.exists():
            print(f"{rel}: no vanilla file, skipped")
            continue
        applied, misses, skipped = apply(etree.parse(str(target), parser).getroot(),
                                         etree.parse(str(patch_file), parser).getroot(), str(rel))
        note = f", {skipped} conditional skipped" if skipped else ""
        print(f"{rel}: {applied} applied, {len(misses)} missed{note}")
        total_misses += misses
    for m in total_misses:
        print("  " + m)
    sys.exit(1 if total_misses else 0)


if __name__ == "__main__":
    main()

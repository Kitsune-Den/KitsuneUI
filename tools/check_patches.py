"""Apply KitsuneUI's XML patches to the vanilla files offline and report any that miss.

A dedicated server only patches what it loads, and it never loads the main-menu
UI (XUi_Menu), so a menu patch that matches nothing would only show up in a
client's log. This applies each patch file to its vanilla counterpart with lxml,
in the game's order (top to bottom), and lists every patch that matched no node.

    python tools/check_patches.py "<7 Days To Die>/Data/Config" [KitsuneUI/Config]
    python tools/check_patches.py <vanilla Config> KitsuneUI/Config KitsuneUI_Practice/Config --loaded KitsuneShugyo

Handles set, setattribute, append, prepend, insertBefore, insertAfter, remove,
removeattribute. <conditional> blocks take their first branch whose mod_loaded()
test passes, counting the mod folders given and any --loaded names as installed;
a condition it can't read is reported as skipped.
"""

import copy
import re
import sys
from pathlib import Path

from lxml import etree

ROOT = Path(__file__).resolve().parent.parent

# Mods treated as installed for <conditional> (mod_loaded tests).
LOADED = set()


def condition(cond):
    """True / False for a mod_loaded() expression (with !, && and ||), None if it's anything else."""
    expr = re.sub(r"mod_loaded\('([^']+)'\)", lambda m: str(m.group(1) in LOADED), cond)
    expr = expr.replace("&&", " and ").replace("||", " or ").replace("!", " not ")
    if not re.fullmatch(r"[\s()]*((True|False|and|or|not)[\s()]*)+", expr):
        return None
    return eval(expr)


def apply(target_root, patch_root, label):
    misses, applied, skipped = [], 0, 0
    for op in patch_root:
        if not isinstance(op.tag, str):
            continue  # comments
        if op.tag == "conditional":
            for branch in op:
                if not isinstance(branch.tag, str):
                    continue
                passed = True if branch.tag == "else" else condition(branch.get("cond", ""))
                if passed is None:
                    skipped += 1
                    break
                if passed:
                    a, m, sk = apply(target_root, branch, label)
                    applied, skipped = applied + a, skipped + sk
                    misses += m
                    break
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
                # Like the game: the xpath names the attribute itself (.../@name).
                if isinstance(n, etree._ElementUnicodeResult) and n.is_attribute:
                    n.getparent().attrib.pop(n.attrname, None)
                else:
                    misses.append(f"{label}: <removeattribute xpath=\"{xp}\"> matched an element; point it at the attribute (.../@name)")
                    break
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
    """Usage: check_patches.py <vanilla Config> [mod Config dir ...] [--loaded Mod1,Mod2]
    Several mod dirs are applied in the order given (the game's load order), so an
    add-on's patches can target what the core theme added."""
    args = sys.argv[1:]
    if "--loaded" in args:
        i = args.index("--loaded")
        LOADED.update(n for n in args[i + 1].split(",") if n)
        del args[i:i + 2]
    vanilla = Path(args[0])
    mods = [Path(p) for p in args[1:]] or [ROOT / "KitsuneUI" / "Config"]
    LOADED.update(m.resolve().parent.name for m in mods)
    parser = etree.XMLParser(remove_comments=False)
    total_misses = []
    rels = sorted({f.relative_to(m) for m in mods for f in m.rglob("*.xml")})
    for rel in rels:
        target = vanilla / rel
        if not target.exists():
            print(f"{rel}: no vanilla file, skipped")
            continue
        root = etree.parse(str(target), parser).getroot()
        for mod in mods:
            patch_file = mod / rel
            if not patch_file.exists():
                continue
            label = f"{mod.parent.name}/{rel}"
            applied, misses, skipped = apply(root, etree.parse(str(patch_file), parser).getroot(), label)
            note = f", {skipped} conditional skipped" if skipped else ""
            print(f"{label}: {applied} applied, {len(misses)} missed{note}")
            total_misses += misses
    for m in total_misses:
        print("  " + m)
    sys.exit(1 if total_misses else 0)


if __name__ == "__main__":
    main()

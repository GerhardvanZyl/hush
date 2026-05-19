"""
One-shot content replacer for the Hush -> Hush rename.

Applies an ordered list of literal string replacements to every tracked text
file (excluding bin/obj/out/node_modules and binary assets).

Run from repo root:  python build/rename-to-hush.py
"""
import os
import subprocess
import sys

# Ordered. Longer / more-specific patterns first so they don't get clobbered
# by shorter substring matches. Each tuple is (old, new).
REPLACEMENTS = [
    # File-path-style identifiers (longest first)
    ("Hush.VSCode.Sidecar", "Hush.VSCode.Sidecar"),
    ("Hush.VSCode",         "Hush.VSCode"),
    ("Hush.Core.Tests",     "Hush.Core.Tests"),
    ("Hush.Core",           "Hush.Core"),
    ("Hush.VS",             "Hush.VS"),

    # Type names that include the brand
    ("HushPackage",         "HushPackage"),
    ("HushCommands",        "HushCommands"),

    # VSCT GUID-symbol names (the underlying GUID is unchanged)
    ("guidHushPackage",     "guidHushPackage"),
    ("guidHushCmdSet",      "guidHushCmdSet"),
    ("HushMenuGroup",                  "HushMenuGroup"),

    # User-visible classifier/format/tagger type names + the F&C
    # classification-type strings ("hush.*" -> "hush.*") and their display
    # names ("Hush Telemetry" -> "Hush Telemetry").
    ("HushClassifierProvider",         "HushClassifierProvider"),
    ("HushClassificationTypes",        "HushClassificationTypes"),
    ("HushClassificationFormats",      "HushClassificationFormats"),
    ("HushClassifier",                 "HushClassifier"),
    ("HushOutliningTaggerProvider",    "HushOutliningTaggerProvider"),
    ("HushOutliningTagger",            "HushOutliningTagger"),
    ("HushTelemetryFormat",            "HushTelemetryFormat"),
    ("HushLoggingFormat",              "HushLoggingFormat"),
    ("HushSignatureFormat",            "HushSignatureFormat"),
    ("HushGuardsFormat",               "HushGuardsFormat"),
    ("HushUserSlotFormatBase",         "HushUserSlotFormatBase"),
    ("HushUserSlot",                   "HushUserSlot"),   # HushUserSlot1Format etc.
    ("\"Hush ",                        "\"Hush "),         # display names: "Hush Telemetry" etc.
    ("\"hush.",                        "\"hush."),         # F&C IDs
    ("$\"Hush ",                       "$\"Hush "),        # interpolated display name in base ctor
    ("\"hush.\"",                      "\"hush.\""),       # classification prefix constant value (if present)
    ("= \"hush.\"",                    "= \"hush.\""),
    ("$\"hush.user{",                  "$\"hush.user{"),

    # Remaining bare brand spellings
    ("Hush",                "Hush"),
    ("hush",                "hush"),
    ("hush-vscode",        "hush-vscode"),
    ("hush-vs",            "hush-vs"),
    ("hush",               "hush"),
    ("Hush Boilerplate",               "Hush"),
]

EXCLUDED_DIRS = {"bin", "obj", "out", "node_modules", ".git", ".vs", ".vscode"}
INCLUDED_EXT = {
    ".cs", ".ts", ".js", ".mjs", ".cjs",
    ".csproj", ".sln", ".vsct", ".pkgdef",
    ".json", ".md", ".ps1", ".py",
    ".vsixmanifest", ".config", ".props", ".targets",
    ".xml", ".yml", ".yaml",
}

def should_visit(path):
    parts = path.replace("\\", "/").split("/")
    return not any(p in EXCLUDED_DIRS for p in parts)

def has_included_ext(name):
    name_l = name.lower()
    return any(name_l.endswith(ext) for ext in INCLUDED_EXT)

def main():
    files_changed = 0
    total_replacements = 0
    for root, dirs, files in os.walk("."):
        dirs[:] = [d for d in dirs if d not in EXCLUDED_DIRS]
        if not should_visit(root):
            continue
        for fname in files:
            if not has_included_ext(fname):
                continue
            full = os.path.join(root, fname)
            try:
                with open(full, "r", encoding="utf-8") as f:
                    content = f.read()
            except UnicodeDecodeError:
                # Skip binary-ish files
                continue
            new_content = content
            for old, new in REPLACEMENTS:
                if old in new_content:
                    new_content = new_content.replace(old, new)
            if new_content != content:
                # Count replacements approximately (per-pattern count)
                diff_count = sum(
                    content.count(old) for old, _ in REPLACEMENTS
                )
                total_replacements += diff_count
                with open(full, "w", encoding="utf-8", newline="") as f:
                    f.write(new_content)
                files_changed += 1
                print(f"  modified: {full}")
    print(f"\n{files_changed} files modified ({total_replacements} substring matches)")
    return 0

if __name__ == "__main__":
    sys.exit(main())

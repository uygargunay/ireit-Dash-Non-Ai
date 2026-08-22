#!/usr/bin/env python3
"""Dependency-free structural checks for the IREI Stage 1 V2 package."""

from __future__ import annotations

import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src" / "IreiMvp.Web"
BLANK = WEB / "Data" / "Templates" / "IREI_MVP_Stage1_V2_Blank_Template.xlsx"
DEMO = WEB / "Data" / "Examples" / "IREI_MVP_Stage1_V2_NGOB_Demo.xlsx"

REQUIRED_SHEETS = {
    "MVP_CONTROL",
    "ASSESSMENT_VERSION",
    "FIELD_HISTORY",
    "CHANGE_EVENT",
    "ACTION_DECISION",
    "DATA_OBLIGATIONS",
    "REPORT_SNAPSHOT",
    "DATA_PROPERTY_MASTER",
    "DATA_UNIT_MIX",
    "DATA_OPERATING_ACTUALS",
    "DATA_DEBT_MASTER",
    "DATA_DEBT_SCHEDULE",
    "DATA_CASH_RESERVES",
    "DATA_AGREEMENTS",
    "DATA_CAPITAL_NEEDS",
    "DASH_01_PORTFOLIO",
    "DASH_02_GOVERNANCE",
    "DASH_03_PROPERTIES",
    "DASH_04_FINANCIAL",
    "DASH_05_MISSION",
    "DASH_06_RISKS_ACTIONS",
    "DASH_07_EVIDENCE_QUALITY",
    "DASH_08_REPORTS_SHARING",
}

PAGE_LABELS = [
    "Portfolio Overview",
    "Organization &amp; Governance",
    "Properties / Portfolio",
    "Financial Resilience",
    "Mission &amp; Public Value",
    "Decisions, Risks &amp; Actions",
    "Evidence &amp; Data Quality",
    "Reports &amp; Authorized Use",
]


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def workbook_info(path: Path) -> tuple[set[str], int, str]:
    if not path.is_file():
        fail(f"missing workbook: {path.relative_to(ROOT)}")
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        if "xl/workbook.xml" not in names or "[Content_Types].xml" not in names:
            fail(f"invalid OOXML workbook: {path.name}")
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        namespace = {"x": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
        sheets = {node.attrib["name"] for node in workbook.findall(".//x:sheet", namespace)}
        formula_count = 0
        for name in names:
            if name.startswith("xl/worksheets/") and name.endswith(".xml"):
                root = ET.fromstring(archive.read(name))
                formula_count += len(root.findall(".//x:f", namespace))
        shared = archive.read("xl/sharedStrings.xml").decode("utf-8", errors="replace") if "xl/sharedStrings.xml" in names else ""
    return sheets, formula_count, shared


def check_workbooks() -> None:
    blank_sheets, blank_formulas, blank_shared = workbook_info(BLANK)
    demo_sheets, demo_formulas, demo_shared = workbook_info(DEMO)
    for name, sheets in (("blank", blank_sheets), ("demo", demo_sheets)):
        missing = REQUIRED_SHEETS - sheets
        if missing:
            fail(f"{name} workbook is missing sheets: {sorted(missing)}")
        if len(sheets) != 40:
            fail(f"{name} workbook should contain 40 sheets; found {len(sheets)}")
    if blank_formulas < 3_000:
        fail(f"blank workbook formula count unexpectedly low: {blank_formulas}")
    if demo_formulas < 2_800:
        fail(f"demo workbook formula count unexpectedly low: {demo_formulas}")
    required_headers = [
        "Version_ID", "History_ID", "Change_ID", "Action_ID", "Obligation_ID",
        "Report_ID", "Property_ID", "Fiscal_Year", "Total_Debt_Service",
    ]
    combined = blank_shared + demo_shared
    for header in required_headers:
        if header not in combined:
            fail(f"required V2 workbook header not found: {header}")


def read(path: Path) -> str:
    if not path.is_file():
        fail(f"missing file: {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


def check_application() -> None:
    index = read(WEB / "wwwroot" / "index.html")
    app = read(WEB / "wwwroot" / "app.js")
    program = read(WEB / "Program.cs")
    settings = read(WEB / "appsettings.json")
    models = read(WEB / "Models.cs")

    for label in PAGE_LABELS:
        if label not in index:
            fail(f"missing page navigation label: {label}")
    if index.count('data-page="') != 8:
        fail("the primary navigation must contain exactly eight pages")
    if "IREI_MVP_Stage1_V2_Blank_Template.xlsx" not in settings:
        fail("V2 blank template is not configured")

    prohibited = {
        "AiMappingAdvisor": ROOT,
        "OPENAI_API_KEY": ROOT,
        "AddHttpClient<Ai": ROOT,
        "overallScore(": WEB / "wwwroot",
        "financialReadinessScore": WEB / "wwwroot",
        "impactReadinessScore": WEB / "wwwroot",
        "/100": WEB / "wwwroot",
    }
    for token, location in prohibited.items():
        files = [location] if location.is_file() else [
            path for path in location.rglob("*")
            if path.is_file()
            and "smarterasp-publish" not in path.parts
            and path.suffix.lower() in {".cs", ".js", ".html", ".json", ".md"}
        ]
        for path in files:
            if token in path.read_text(encoding="utf-8", errors="ignore"):
                fail(f"prohibited token {token!r} found in {path.relative_to(ROOT)}")

    required_routes = [
        "/api/submissions/{id}/approve",
        "/api/submissions/{id}/actions",
        "/api/submissions/{id}/obligations",
        "/api/submissions/{id}/evidence",
        "/api/submissions/{id}/reports",
        "/reports/{reportId}/approve",
        "/reports/{reportId}/share",
        "/reports/{reportId}/download",
    ]
    for route in required_routes:
        if route not in program:
            fail(f"missing API route: {route}")

    if "ArtifactPath" not in models or "ArtifactAvailable" not in models:
        fail("private report path/public availability split is missing")
    if re.search(r"\bNGO B\b", app + index, flags=re.IGNORECASE):
        fail("organization-specific demo wording is hard-coded in the UI")


def check_source_inventory() -> None:
    required = [
        "DashboardMetricsFactory.cs",
        "VersionComparisonEngine.cs",
        "V2WorkbookSync.cs",
        "WorkbookDataView.cs",
        "SubmissionStore.cs",
    ]
    for name in required:
        if not (WEB / name).is_file():
            fail(f"missing V2 source component: {name}")
    for removed in ["AiMappingAdvisor.cs", "Data/Profiles/ngo-b-demo.seed.json"]:
        if (WEB / removed).exists():
            fail(f"legacy AI/demo component still exists: {removed}")


def main() -> None:
    check_workbooks()
    check_source_inventory()
    check_application()
    print("IREI Stage 1 V2 structural verification passed.")


if __name__ == "__main__":
    main()

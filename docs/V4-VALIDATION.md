# V4.2 validation report

Validation uses the four supplied files as the implementation contract, with the populated workbook as the financial reference. The implementation is a controlled working pilot. The tests below do not approve the draft methods, source mappings or external reports.

## Verified numerical controls

All comparisons use unrounded decimal values. Money tolerance is $0.01; ratio tolerance is 0.000001. The console suite also reconciles more than 200 available cells in `41_METRIC_RESULTS`, all three registered 55/56 stress cases and all eight 57 option rows, including missing-value behavior.

| Control | Backend expected value |
| --- | ---: |
| FY2026–27 revenue | 12,696,486.415908122 |
| FY2026–27 running costs | 8,614,110.832749803 |
| FY2026–27 NOI | 4,082,375.58315832 |
| FY2026–27 payments | 2,591,047 |
| FY2026–27 DSCR | 1.5755698693070097 |
| FY2026–27 signed cash | 1,491,328.5831583198 |
| FY2026–27 closing reserve | 2,312,308.7558795176 |
| FY2026–27 homes | 715 projected |
| FY2029–30 DSCR | 0.9958406776765482 |
| FY2029–30 cash shortfall | 57,113.97156585194 |
| FY2029–30 placeholder subsidy | 3,318,983 |
| FY2029–30 NOI without placeholder | 10,355,460.028434148 |
| FY2029–30 shortfall without placeholder | 3,376,096.971565852 |
| FY2029–30 DSCR without placeholder | 0.7541358950360945 |
| FY2029–30 base target cushion gap | 2,803,425.371566 |
| FY2029–30 excluded-subsidy target gap | 6,122,408.371566 |

## Acceptance coverage

| Contract / rule | Observable check | Result |
| --- | --- | --- |
| Sections 6, 8; BR-V4-059 | Financial controls and cash shortfall versus target gap | Pass |
| 55/56, 57, 58 | Registered stress/option parity; missing critical capacity gate withholds amount | Pass |
| Grain isolation | Additional building debt leaves selected portfolio payments unchanged | Pass |
| BR-V4-042/043 | Reporting year changes current context/tiles together | Pass |
| BR-V4-027–030 | Budget remains Projected; explicit zero funding remains zero; missing debt has no ratio/severity | Pass |
| Severity boundary | 0.99 High; 1.00/1.19 Medium; 1.20 no issue | Pass |
| BR-V4-063 | Near-one DSCR displays 0.996x; building detail collapsed; 16 rows on expansion | Pass |
| BR-V4-064 | Full finance period coverage is Confirmed; 10/11 debt coverage is Partly confirmed; projection/review unchanged | Pass |
| Placeholder/option tests | Subsidy removed once; confirmation restores once; base confirmation adds zero; bridge/recurring effects differ | Pass |
| Sources/mapping | Direct formula chain resolves to raw numeric input; proposed review status retained; source drawer works | Pass |
| Register counts | 16 buildings; 16 Open, 2 Resolved, 2 accepted exceptions | Pass |
| Second organization | Independently populated second workbook imports its own identifiers/inputs and produces 2.0x coverage without NGO B page text | Pass |
| BR-V4-066–070 | Immutable baseline, shared proposed action, append-only decision/outcomes, null unmeasured observation, scope/method comparability guard | Pass |
| BR-V4-065/071/072 | Pilot approval/release blocked; contribution only; no program cohort comparison exposed | Pass |
| HTTP authorization | Missing access key rejected; seven pages and evidence require the pilot header | Pass |
| Browser | Seven desktop screens, mobile width, source drawer, year switch, bridge result and persisted shared action; no JavaScript errors | Pass |
| Maturity conflict | V18 JP!A32 and 2025 summary X26 are absent from the attached structured fixture | Waiting on data |
| Already-passed terms | Historical instrument candidates plus explicit reporting as-of are not staged | Waiting on data |
| Exact Revision 4 Page 1 artwork | Separate PNG/original logo file not attached; written Page 1 requirements and embedded PDF logo used | Reference gap recorded |
| Production methods/identity roles | Shared-key pilot gate; production approval, role separation and external delivery intentionally unavailable | Deferred |

The .NET suite passes 340 assertions. HTTP integration checks pass. Browser screenshots were rendered and reviewed locally; the initial snapshot preserves selected working-year context rather than implying an audited result. Two existing nullable warnings in `XlsxWorkbookService` are unrelated to V4 and do not prevent the build.

The existing V2 structural check originally failed on a hardcoded key in two legacy deployment configurations. Those keys were cleared, preserving environment-provided authorization. The V2 structural verification now passes. V3 source/routes are retained and its JavaScript syntax is checked.

## Import findings and scope limits

The source schema contains field-value inconsistencies in review-oriented exception metadata. These are retained as import findings rather than silently relabeled or converted to financial facts. The displayed 16 open questions are counted from eligible `51_EXCEPTIONS` rows independently from import findings.

Legacy arbitrary-formula input workbooks require mapping through the existing workflow. This importer accepts the finalized V4.2 structured template and registered calculations. It does not evaluate arbitrary source workbook formulas, optimize financing, approve narrative automatically, produce external report packages or assign a readiness score.

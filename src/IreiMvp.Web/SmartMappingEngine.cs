using System.Globalization;
using System.Text.RegularExpressions;

namespace IreiMvp.Web;

public sealed class SmartMappingEngine
{
    private static readonly Regex YearRangeRegex = new(
        @"(?<!\d)((?:19|20)\d{2})\s*[-/]\s*((?:19|20)?\d{2,4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SingleYearRegex = new(
        @"^(?:19|20)\d{2}$",
        RegexOptions.Compiled);

    private static readonly Regex AggregateRegex = new(
        @"\b(summary|consolidated|portfolio|cash[\s-]*flow|all\s+buildings|all\s+properties|organization)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RevenueRegex = new(
        @"\b(rent|revenue|income|subsidy|contribution|grant|donation|dues|recoveries|project\s+fees?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ExpenseRegex = new(
        @"\b(expense|cost|charge|salary|salaries|wage|maintenance|repair|utilities|utility|tax|insurance|audit|legal|administration|supplies|cleaning|management|consulting|reserve)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DebtServiceRegex = new(
        @"\b(mortgage\s+payments?|debt\s+service|mortgage\s+principal|mortgage\s+interest|bond\s+principal|bond\s+interest)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnitFormulaRegex = new(
        @"\*([1-9]\d{0,3})\*12\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<MappingBuildResult> BuildAsync(
        WorkbookSnapshot source,
        WorkbookSnapshot template,
        string organizationName,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        var result = new MappingBuildResult();

        var sourceLog = FindTargetTable(template, "Source_ID");
        var propertyTable = FindTargetTable(template, "Property_ID", "Property_Name", "Included?");
        var unitTable = FindTargetTable(template, "Record_ID", "Unit_Count", "Affordability_Band");
        var operatingTable = FindTargetTable(template, "Record_ID", "Source_Line_Item", "IREI_Category", "Amount");
        var debtTable = FindTargetTable(template, "Debt_ID", "Principal_Amount", "Source_Annual_Debt_Service");
        var flagTable = FindTargetTable(template, "Flag_ID", "Issue / Assumption", "Status");

        if (source.HasSheet("DATA_PROPERTY_MASTER") &&
            source.HasSheet("DATA_METRIC_LONG"))
        {
            AddCanonicalV2Mappings(result, source, template, organizationName);
            if (!HasSourceLogRows(source))
            {
                AddSourceLog(
                    result.Patches,
                    sourceLog,
                    originalFileName,
                    source.Sheets.Count);
            }

            result.ReportingPeriod = ReadControlValue(source, "Reporting Fiscal Year");
            result.SourceCutoffDate = ReadControlValue(source, "Source Cutoff Date");
            AddOutputMetadata(
                result.Patches,
                template,
                organizationName,
                originalFileName,
                result.ReportingPeriod);

            var canonicalErrors = source.Sheets.Values
                .SelectMany(sheet => sheet.Cells.Values)
                .Count(cell => cell.Value is string text && text.StartsWith("#", StringComparison.Ordinal));
            if (canonicalErrors > 0)
            {
                result.Warnings.Add(
                    $"The uploaded workbook contains {canonicalErrors} cached Excel error value(s). Review the affected source formulas.");
            }

            WriteWarnings(result.Patches, flagTable, result.Warnings);
            return Task.FromResult(result);
        }

        AddSourceLog(
            result.Patches,
            sourceLog,
            originalFileName,
            source.Sheets.Count);

        var statements = source.Sheets.Values
            .Select(AnalyzeStatement)
            .Where(profile => profile is not null)
            .Cast<StatementProfile>()
            .ToList();

        var propertyStatements = statements
            .Where(profile => !profile.IsAggregate && profile.FinancialHitCount >= 5)
            .OrderBy(profile => profile.Sheet.Name)
            .ToList();

        var propertyRows = AddProperties(
            result,
            propertyTable,
            propertyStatements,
            originalFileName);

        var portfolioStatement = statements
            .Where(profile => profile.IsAggregate)
            .OrderByDescending(ScorePortfolioStatement)
            .FirstOrDefault()
            ?? statements
                .OrderByDescending(ScorePortfolioStatement)
                .FirstOrDefault();

        var reportingPeriod = "";
        var portfolioUnitCount = 0;

        if (portfolioStatement is not null)
        {
            var selectedYear = SelectReportingYear(portfolioStatement);
            if (selectedYear is not null)
            {
                reportingPeriod = selectedYear.Label;
                result.ReportingPeriod = reportingPeriod;

                AddOperatingRows(
                    result,
                    operatingTable,
                    portfolioStatement,
                    selectedYear,
                    organizationName,
                    originalFileName);

                var unitCandidate = statements
                    .Where(profile =>
                        profile.IsAggregate &&
                        profile.UnitRowCount > 0)
                    .Select(profile => new
                    {
                        Profile = profile,
                        Year = profile.Years
                            .OrderBy(year =>
                                Math.Abs(year.EndYear - selectedYear.EndYear))
                            .FirstOrDefault()
                    })
                    .Where(candidate => candidate.Year is not null)
                    .Select(candidate => new
                    {
                        candidate.Profile,
                        Year = candidate.Year!,
                        Count = FindPortfolioUnitCount(
                            candidate.Profile,
                            candidate.Year!)
                    })
                    .OrderByDescending(candidate => candidate.Count)
                    .ThenBy(candidate => candidate.Profile.ErrorCount)
                    .FirstOrDefault();

                if (unitCandidate is not null &&
                    unitCandidate.Count > 0)
                {
                    portfolioUnitCount = AddPortfolioUnitMix(
                        result.Patches,
                        unitTable,
                        unitCandidate.Profile,
                        unitCandidate.Year,
                        organizationName,
                        originalFileName);
                }
            }
        }

        AddDebtRows(
            result,
            debtTable,
            statements,
            source.Sheets.Values.ToList(),
            organizationName,
            originalFileName);

        if (statements.Count == 0)
        {
            AddGenericTableMappings(
                result,
                source,
                template);
        }

        var sourceErrorCount = source.Sheets.Values
            .SelectMany(sheet => sheet.Cells.Values)
            .Count(cell =>
                cell.Value is string text &&
                text.StartsWith("#", StringComparison.Ordinal));

        if (sourceErrorCount > 0)
        {
            result.Warnings.Add(
                $"The uploaded workbook contains {sourceErrorCount} cached Excel error value(s). Review the affected formulas in the source workbook.");
        }

        if (propertyStatements.Count == 0)
        {
            result.Warnings.Add(
                "No property-level wide financial statement tabs were confidently identified.");
        }

        var inferredPropertyUnits = propertyRows.Sum(row => row.PrimaryUnits);
        if (portfolioUnitCount > 0 && inferredPropertyUnits < portfolioUnitCount)
        {
            result.Warnings.Add(
                $"The portfolio statement reports {portfolioUnitCount:N0} units, while only {inferredPropertyUnits:N0} units were inferred at property level. Property unit counts remain incomplete.");
        }

        if (portfolioStatement is not null)
        {
            result.Warnings.Add(
                $"Operating lines were standardized from '{portfolioStatement.Sheet.Name}' for {reportingPeriod}. Confirm line-item categories before external use.");
        }

        if (portfolioUnitCount == 0)
        {
            result.Warnings.Add(
                "A reliable portfolio unit total was not found in the uploaded workbook.");
        }

        result.Warnings.Add(
            "Affordability, agreement, reserve and capital-needs evidence was not fully identified in this source workbook and remains subject to review.");

        AddOutputMetadata(
            result.Patches,
            template,
            organizationName,
            originalFileName,
            reportingPeriod);

        WriteWarnings(result.Patches, flagTable, result.Warnings);

        return Task.FromResult(result);
    }

    public static int FindLikelyHeaderRow(SheetSnapshot sheet)
    {
        var bestRow = 0;
        var bestScore = 0;

        for (var row = 1; row <= Math.Min(sheet.MaxRow, 30); row++)
        {
            var textCount = 0;
            var nonBlankCount = 0;

            for (var column = 1; column <= Math.Min(sheet.MaxColumn, 50); column++)
            {
                var value = sheet.GetValue(row, column);
                if (value is null || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    continue;
                }

                nonBlankCount++;
                if (value is string)
                {
                    textCount++;
                }
            }

            var score = textCount * 3 + nonBlankCount;
            if (textCount >= 2 && score > bestScore)
            {
                bestScore = score;
                bestRow = row;
            }
        }

        return bestRow;
    }

    private static StatementProfile? AnalyzeStatement(SheetSnapshot sheet)
    {
        var bestYearRow = 0;
        var bestYears = new List<YearColumn>();

        for (var row = 1; row <= Math.Min(sheet.MaxRow, 25); row++)
        {
            var years = new List<YearColumn>();

            for (var column = 1; column <= Math.Min(sheet.MaxColumn, 60); column++)
            {
                var value = sheet.GetValue(row, column);
                if (TryParseYear(value, out var endYear, out var label))
                {
                    years.Add(new YearColumn(column, endYear, label));
                }
            }

            if (years.Count >= 3 && years.Count > bestYears.Count)
            {
                bestYearRow = row;
                bestYears = years;
            }
        }

        if (bestYearRow == 0)
        {
            return null;
        }

        var firstYearColumn = bestYears.Min(year => year.Column);
        var labelColumn = 1;
        var bestLabelCount = -1;

        for (var column = 1; column < firstYearColumn; column++)
        {
            var count = 0;
            for (var row = bestYearRow + 1;
                 row <= Math.Min(sheet.MaxRow, bestYearRow + 140);
                 row++)
            {
                if (sheet.GetValue(row, column) is string text &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    count++;
                }
            }

            if (count > bestLabelCount)
            {
                bestLabelCount = count;
                labelColumn = column;
            }
        }

        var title = FindTitle(sheet, bestYearRow);
        var financialHits = 0;
        var totalRows = 0;
        var unitRows = 0;

        for (var row = bestYearRow + 1; row <= sheet.MaxRow; row++)
        {
            var label = CleanText(sheet.GetValue(row, labelColumn));
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (label.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                totalRows++;
            }

            if (Normalize(label) is "units" or "totalunits")
            {
                unitRows++;
            }

            if (RevenueRegex.IsMatch(label) ||
                ExpenseRegex.IsMatch(label) ||
                DebtServiceRegex.IsMatch(label) ||
                label.Contains("cash flow", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("NOI", StringComparison.OrdinalIgnoreCase))
            {
                financialHits++;
            }
        }

        if (financialHits < 5)
        {
            return null;
        }

        var errorCount = sheet.Cells.Values.Count(cell =>
            cell.Value is string text &&
            text.StartsWith("#", StringComparison.Ordinal));

        return new StatementProfile
        {
            Sheet = sheet,
            YearRow = bestYearRow,
            Years = bestYears,
            LabelColumn = labelColumn,
            Title = string.IsNullOrWhiteSpace(title) ? sheet.Name : title,
            IsAggregate = AggregateRegex.IsMatch($"{sheet.Name} {title}"),
            FinancialHitCount = financialHits,
            TotalRowCount = totalRows,
            UnitRowCount = unitRows,
            ErrorCount = errorCount
        };
    }

    private static string FindTitle(SheetSnapshot sheet, int yearRow)
    {
        for (var row = 1; row < yearRow; row++)
        {
            for (var column = 1; column <= Math.Min(sheet.MaxColumn, 6); column++)
            {
                var text = CleanText(sheet.GetValue(row, column));
                if (string.IsNullOrWhiteSpace(text) ||
                    string.Equals(text, "Fiscal Year", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!Regex.IsMatch(text, @"^[\d\s$%*+./-]+$"))
                {
                    return text;
                }
            }
        }

        return sheet.Name.Trim();
    }

    private static double ScorePortfolioStatement(StatementProfile profile)
    {
        var score = profile.FinancialHitCount +
                    profile.TotalRowCount * 2 +
                    profile.UnitRowCount * 15 -
                    profile.ErrorCount * 5.0;

        if (profile.IsAggregate)
        {
            score += 50;
        }

        return score;
    }

    private static YearColumn? SelectReportingYear(StatementProfile profile)
    {
        var currentYear = DateTime.UtcNow.Year;

        return profile.Years
            .Where(year => year.EndYear <= currentYear)
            .OrderByDescending(year => year.EndYear)
            .FirstOrDefault()
            ?? profile.Years.OrderBy(year => year.EndYear).FirstOrDefault();
    }

    private static List<PropertyInference> AddProperties(
        MappingBuildResult result,
        TargetTable? table,
        IReadOnlyList<StatementProfile> propertyStatements,
        string originalFileName)
    {
        var inferences = new List<PropertyInference>();
        if (table is null)
        {
            result.Warnings.Add(
                "The target property table could not be located in the blank template.");
            return inferences;
        }

        var row = table.DataStartRow;
        var index = 1;

        foreach (var profile in propertyStatements.Take(200))
        {
            var unitInference = InferUnits(profile);
            var firstPositiveYear = FindFirstPositiveRevenueYear(profile);
            var firstYear = profile.Years.OrderBy(year => year.EndYear).FirstOrDefault();
            var status = firstPositiveYear is not null &&
                         firstYear is not null &&
                         firstPositiveYear.EndYear >= firstYear.EndYear + 2
                ? "New Dev"
                : "Active";

            var propertyId = $"PROP-{index:000}";
            var city = ParseCity(profile.Title);

            WriteRecord(result.Patches, table, row, new Dictionary<string, object?>
            {
                ["Property_ID"] = propertyId,
                ["Property_Name"] = profile.Title,
                ["City"] = city,
                ["Status"] = status,
                ["Included?"] = "Yes",
                ["Final Classification"] = status == "New Dev" ? "New Build" : "Stable / Review",
                ["Register Bucket Mapping"] = status == "New Dev" ? "Development" : "Existing",
                ["Total Units"] = unitInference.Primary > 0 ? unitInference.Primary : null,
                ["Modeled Unit Count"] = unitInference.Primary > 0 ? unitInference.Primary : null,
                ["Alternate Unit Count"] = unitInference.Alternate > 0 ? unitInference.Alternate : null,
                ["Unit Conflict?"] = unitInference.Conflict ? "Yes" : "No",
                ["Start Year"] = firstPositiveYear?.EndYear,
                ["Source Confidence"] = unitInference.Primary > 0 ? "Medium" : "Partial",
                ["Source Document ID"] = "SRC-002",
                ["Review Note"] = unitInference.Note,
                ["Data Status"] = unitInference.Conflict ? "Review" : "Source-based"
            });

            if (unitInference.Conflict)
            {
                result.Warnings.Add(
                    $"{profile.Title}: multiple possible unit counts were identified ({unitInference.Primary:N0} and {unitInference.Alternate:N0}).");
            }

            inferences.Add(new PropertyInference(
                propertyId,
                profile.Title,
                unitInference.Primary,
                status));

            row++;
            index++;
        }

        return inferences;
    }

    private static int FindPortfolioUnitCount(
        StatementProfile profile,
        YearColumn year)
    {
        for (var row = profile.YearRow + 1;
             row <= profile.Sheet.MaxRow;
             row++)
        {
            var label = CleanText(
                profile.Sheet.GetValue(row, profile.LabelColumn));
            var normalized = Normalize(label);

            if (normalized is not
                ("units" or "totalunits" or "modeledunits"))
            {
                continue;
            }

            var count = ToInt(profile.Sheet.GetValue(row, year.Column));
            if (count > 0)
            {
                return count;
            }
        }

        return 0;
    }

    private static int AddPortfolioUnitMix(
        List<CellPatch> patches,
        TargetTable? table,
        StatementProfile profile,
        YearColumn year,
        string organizationName,
        string originalFileName)
    {
        if (table is null)
        {
            return 0;
        }

        for (var row = profile.YearRow + 1; row <= profile.Sheet.MaxRow; row++)
        {
            var label = CleanText(profile.Sheet.GetValue(row, profile.LabelColumn));
            var normalized = Normalize(label);
            if (normalized is not ("units" or "totalunits" or "modeledunits"))
            {
                continue;
            }

            var count = ToInt(profile.Sheet.GetValue(row, year.Column));
            if (count <= 0)
            {
                continue;
            }

            WriteRecord(patches, table, table.DataStartRow, new Dictionary<string, object?>
            {
                ["Record_ID"] = "UNIT-001",
                ["Property_ID"] = "PORTFOLIO",
                ["Property_Name"] = organizationName,
                ["Unit_Group_ID"] = "PORTFOLIO-TOTAL",
                ["Unit_Type"] = "Source-reported total",
                ["Affordability_Band"] = "Not classified",
                ["Unit_Count"] = count,
                ["Affordable_Units"] = 0,
                ["Market_Units"] = 0,
                ["Supportive_Units"] = 0,
                ["Source_File_ID"] = "SRC-002",
                ["Review_Status"] = "Review"
            });

            return count;
        }

        return 0;
    }

    private static void AddOperatingRows(
        MappingBuildResult result,
        TargetTable? table,
        StatementProfile profile,
        YearColumn year,
        string organizationName,
        string originalFileName)
    {
        if (table is null)
        {
            result.Warnings.Add(
                "The target operating table could not be located in the blank template.");
            return;
        }

        var targetRow = table.DataStartRow;
        var recordNumber = 1;
        string? currentSection = null;

        for (var row = profile.YearRow + 1;
             row <= profile.Sheet.MaxRow && targetRow <= 1006;
             row++)
        {
            var label = CleanText(profile.Sheet.GetValue(row, profile.LabelColumn));
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var amountValue = profile.Sheet.GetValue(row, year.Column);
            var amount = ToDecimalOrNull(amountValue);

            if (IsSectionHeading(label, amount))
            {
                currentSection = InferSection(label);
                continue;
            }

            if (label.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith("NET ", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("ENDING BALANCE", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("CASH FLOW", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (amount is null)
            {
                if (amountValue is string text &&
                    text.StartsWith("#", StringComparison.Ordinal))
                {
                    result.Warnings.Add(
                        $"{profile.Sheet.Name}!{XlsxAddress.ToCellReference(row, year.Column)} contains {text}.");
                }

                continue;
            }

            if (DebtServiceRegex.IsMatch(label))
            {
                continue;
            }

            var category = InferCategory(label, currentSection);
            if (category is null)
            {
                continue;
            }

            WriteRecord(result.Patches, table, targetRow, new Dictionary<string, object?>
            {
                ["Record_ID"] = $"OPR-{recordNumber:000}",
                ["Property_ID"] = "PORTFOLIO",
                ["Property_Name"] = organizationName,
                ["Fiscal_Year"] = year.Label,
                ["Period"] = "Annual",
                ["Scenario"] = "Source case",
                ["Actual_Budget_Projection"] = "Source workbook",
                ["Source_Line_Item"] = label,
                ["IREI_Category"] = category,
                ["Amount"] = amount.Value,
                ["Cash_or_Accrual"] = "Not confirmed",
                ["Source_File_ID"] = "SRC-002",
                ["Review_Status"] = "Deterministic pattern mapped"
            });

            targetRow++;
            recordNumber++;
        }
    }

    private static void AddDebtRows(
        MappingBuildResult result,
        TargetTable? table,
        IReadOnlyList<StatementProfile> statements,
        IReadOnlyList<SheetSnapshot> allSheets,
        string organizationName,
        string originalFileName)
    {
        if (table is null)
        {
            result.Warnings.Add(
                "The target debt table could not be located in the blank template.");
            return;
        }

        var debtRecords = ExtractDebtMatrices(allSheets);
        var targetRow = table.DataStartRow;
        var debtNumber = 1;

        foreach (var debt in debtRecords.Take(200))
        {
            WriteRecord(result.Patches, table, targetRow, new Dictionary<string, object?>
            {
                ["Debt_ID"] = $"DEBT-{debtNumber:000}",
                ["Property_ID"] = debt.PropertyId,
                ["Property_Name"] = debt.PropertyName,
                ["Included?"] = "Yes",
                ["Debt_Type"] = debt.DebtType,
                ["Lender / Program"] = debt.Lender,
                ["Principal_Amount"] = debt.Principal > 0 ? debt.Principal : null,
                ["Interest_Rate"] = debt.InterestRate,
                ["Term_Years"] = debt.TermYears,
                ["Amortization_Years"] = debt.AmortizationYears,
                ["Source_Annual_Debt_Service"] = debt.AnnualDebtService > 0
                    ? debt.AnnualDebtService
                    : null,
                ["Source_File_ID"] = "SRC-002",
                ["Review_Status"] = "Source-based",
                ["Notes"] = debt.Note
            });

            targetRow++;
            debtNumber++;
        }

        if (debtRecords.Count > 0)
        {
            return;
        }

        var bestDebt = statements
            .Select(profile => new
            {
                Profile = profile,
                Year = SelectReportingYear(profile)
            })
            .Where(item => item.Year is not null)
            .Select(item => new
            {
                item.Profile,
                Year = item.Year!,
                Amount = FindStatementDebtService(item.Profile, item.Year!)
            })
            .OrderByDescending(item => item.Amount)
            .FirstOrDefault();

        if (bestDebt is null || bestDebt.Amount <= 0)
        {
            result.Warnings.Add(
                "No reliable debt schedule or annual debt-service line was found.");
            return;
        }

        WriteRecord(result.Patches, table, targetRow, new Dictionary<string, object?>
        {
            ["Debt_ID"] = "DEBT-001",
            ["Property_ID"] = "PORTFOLIO",
            ["Property_Name"] = organizationName,
            ["Included?"] = "Yes",
            ["Debt_Type"] = "Portfolio debt service",
            ["Lender / Program"] = "Not confirmed",
            ["Source_Annual_Debt_Service"] = bestDebt.Amount,
            ["Source_File_ID"] = "SRC-002",
            ["Review_Status"] = "Partial",
            ["Notes"] = $"Annual debt service extracted from {bestDebt.Profile.Sheet.Name} for {bestDebt.Year.Label}; principal, rate and term were not identified."
        });

        result.Warnings.Add(
            "Annual debt service was identified, but principal, interest rate and term require a separate debt or amortization schedule.");
    }

    private static decimal FindStatementDebtService(
        StatementProfile profile,
        YearColumn year)
    {
        var total = 0m;

        for (var row = profile.YearRow + 1; row <= profile.Sheet.MaxRow; row++)
        {
            var label = CleanText(profile.Sheet.GetValue(row, profile.LabelColumn));
            if (!DebtServiceRegex.IsMatch(label))
            {
                continue;
            }

            var amount = ToDecimalOrNull(profile.Sheet.GetValue(row, year.Column));
            if (amount is not null)
            {
                total += Math.Abs(amount.Value);
            }
        }

        return total;
    }

    private static List<DebtInference> ExtractDebtMatrices(
        IReadOnlyList<SheetSnapshot> sheets)
    {
        var result = new List<DebtInference>();

        foreach (var sheet in sheets)
        {
            for (var row = 1; row <= Math.Min(sheet.MaxRow, 12); row++)
            {
                for (var column = 1; column <= sheet.MaxColumn; column++)
                {
                    var header = Normalize(CleanText(sheet.GetValue(row, column)));
                    if (header != "loanamount")
                    {
                        continue;
                    }

                    var propertyName = FindTextAbove(sheet, row, column);
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        propertyName = sheet.Name.Trim();
                    }

                    var terms = FindNearbyText(sheet, row, column, 4);
                    var principal = FindNearbyNumber(sheet, row + 1, column, 5);
                    var interestRate = ParsePercent(terms);
                    var termYears = ParseYears(terms, "term");
                    var amortizationYears = ParseYears(terms, "amort");
                    var annualDebtService = FindFirstAnnualPayment(
                        sheet,
                        row + 1,
                        column);

                    result.Add(new DebtInference
                    {
                        PropertyId = $"PROP-{result.Count + 1:000}",
                        PropertyName = propertyName,
                        DebtType = "Mortgage / loan",
                        Lender = "Not confirmed",
                        Principal = principal,
                        InterestRate = interestRate,
                        TermYears = termYears,
                        AmortizationYears = amortizationYears,
                        AnnualDebtService = annualDebtService,
                        Note = $"Extracted from repeated loan-summary pattern on sheet '{sheet.Name}'."
                    });
                }
            }
        }

        return result
            .GroupBy(item => item.PropertyName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddCanonicalV2Mappings(
        MappingBuildResult result,
        WorkbookSnapshot source,
        WorkbookSnapshot template,
        string organizationName)
    {
        string[] inputSheets =
        [
            "STG_SOURCE_INTAKE",
            "DATA_PROPERTY_MASTER",
            "DATA_UNIT_MIX",
            "DATA_RENT_ROLL",
            "DATA_OPERATING_ACTUALS",
            "DATA_DEBT_MASTER",
            "DATA_CASH_RESERVES",
            "DATA_AGREEMENTS",
            "DATA_CAPITAL_NEEDS",
            "ASSUMPTIONS_FLAGS",
            "DATA_DEBT_SCHEDULE",
            "DATA_OBLIGATIONS"
        ];

        foreach (var sheetName in inputSheets)
        {
            if (!source.Sheets.TryGetValue(sheetName, out var sourceSheet) ||
                !template.Sheets.TryGetValue(sheetName, out var targetSheet))
            {
                continue;
            }

            CopyCanonicalTable(result.Patches, sourceSheet, targetSheet);
        }

        if (source.Sheets.TryGetValue("MVP_CONTROL", out var sourceControl) &&
            template.Sheets.TryGetValue("MVP_CONTROL", out var targetControl))
        {
            for (var row = 5; row <= Math.Min(sourceControl.MaxRow, 25); row++)
            {
                var value = sourceControl.GetValue(row, 2);
                if (IsBlank(value))
                {
                    continue;
                }

                result.Patches.Add(new CellPatch
                {
                    Sheet = targetControl.Name,
                    Cell = $"B{row}",
                    Value = value
                });
            }

            result.Patches.RemoveAll(patch =>
                string.Equals(patch.Sheet, targetControl.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(patch.Cell, "B18", StringComparison.OrdinalIgnoreCase));
            result.Patches.Add(new CellPatch
            {
                Sheet = targetControl.Name,
                Cell = "B18",
                Value = organizationName
            });
        }
    }

    private static void CopyCanonicalTable(
        List<CellPatch> patches,
        SheetSnapshot source,
        SheetSnapshot target)
    {
        var sourceHeaderRow = FindLikelyHeaderRow(source);
        var targetHeaderRow = FindLikelyHeaderRow(target);
        if (sourceHeaderRow == 0 || targetHeaderRow == 0)
        {
            return;
        }

        var sourceHeaders = ReadHeaders(source, sourceHeaderRow);
        var targetHeaders = ReadHeaders(target, targetHeaderRow);
        var mappings = targetHeaders
            .Select(targetHeader => new
            {
                TargetColumn = targetHeader.Key,
                SourceColumn = sourceHeaders
                    .Where(sourceHeader => Normalize(sourceHeader.Value) == Normalize(targetHeader.Value))
                    .Select(sourceHeader => sourceHeader.Key)
                    .FirstOrDefault()
            })
            .Where(mapping => mapping.SourceColumn > 0)
            .ToList();

        if (mappings.Count < 2)
        {
            return;
        }

        var targetRow = targetHeaderRow + 1;
        for (var sourceRow = sourceHeaderRow + 1;
             sourceRow <= source.MaxRow && targetRow <= 2006;
             sourceRow++)
        {
            var rowValues = mappings
                .Select(mapping => source.GetValue(sourceRow, mapping.SourceColumn))
                .ToList();
            if (rowValues.All(IsBlank))
            {
                continue;
            }

            foreach (var mapping in mappings)
            {
                var value = source.GetValue(sourceRow, mapping.SourceColumn);
                if (IsBlank(value) ||
                    !string.IsNullOrWhiteSpace(target.GetCell(targetRow, mapping.TargetColumn)?.Formula))
                {
                    continue;
                }

                patches.Add(new CellPatch
                {
                    Sheet = target.Name,
                    Cell = XlsxAddress.ToCellReference(targetRow, mapping.TargetColumn),
                    Value = value
                });
            }

            targetRow++;
        }
    }

    private static string ReadControlValue(WorkbookSnapshot source, string controlName)
    {
        if (!source.Sheets.TryGetValue("MVP_CONTROL", out var sheet))
        {
            return "";
        }

        for (var row = 1; row <= sheet.MaxRow; row++)
        {
            if (string.Equals(CleanText(sheet.GetValue(row, 1)), controlName, StringComparison.OrdinalIgnoreCase))
            {
                return CleanText(sheet.GetValue(row, 2));
            }
        }

        return "";
    }

    private static bool HasSourceLogRows(WorkbookSnapshot source)
    {
        if (!source.Sheets.TryGetValue("STG_SOURCE_INTAKE", out var sheet))
        {
            return false;
        }

        var headerRow = FindLikelyHeaderRow(sheet);
        if (headerRow == 0)
        {
            return false;
        }

        for (var row = headerRow + 1; row <= sheet.MaxRow; row++)
        {
            if (!IsBlank(sheet.GetValue(row, 1)))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddGenericTableMappings(
        MappingBuildResult result,
        WorkbookSnapshot source,
        WorkbookSnapshot template)
    {
        var targetTables = DiscoverTargetTables(template).ToList();
        var mappedTargets = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var mappedAny = false;

        foreach (var sourceSheet in source.Sheets.Values)
        {
            var sourceHeaderRow = FindLikelyHeaderRow(sourceSheet);
            if (sourceHeaderRow == 0)
            {
                continue;
            }

            var sourceHeaders = ReadHeaders(sourceSheet, sourceHeaderRow);
            if (sourceHeaders.Count < 2)
            {
                continue;
            }

            TargetTable? bestTarget = null;
            List<(int SourceColumn, int TargetColumn)> bestMappings = [];

            foreach (var target in targetTables)
            {
                if (mappedTargets.Contains(target.Sheet.Name))
                {
                    continue;
                }

                var mappings =
                    new List<(int SourceColumn, int TargetColumn)>();

                foreach (var targetHeader in target.Columns)
                {
                    var sourceColumn = sourceHeaders
                        .Where(header =>
                            Normalize(header.Value) ==
                            Normalize(targetHeader.Key))
                        .Select(header => header.Key)
                        .FirstOrDefault();

                    if (sourceColumn > 0)
                    {
                        mappings.Add((
                            sourceColumn,
                            targetHeader.Value));
                    }
                }

                if (mappings.Count < 2)
                {
                    continue;
                }

                if (bestTarget is null || mappings.Count > bestMappings.Count)
                {
                    bestTarget = target;
                    bestMappings = mappings;
                }
            }

            if (bestTarget is null)
            {
                continue;
            }

            var targetRow = bestTarget.DataStartRow;
            var rowsWritten = 0;

            for (var sourceRow = sourceHeaderRow + 1;
                 sourceRow <= sourceSheet.MaxRow &&
                 targetRow <= 506;
                 sourceRow++)
            {
                var values = bestMappings
                    .Select(mapping =>
                        sourceSheet.GetValue(
                            sourceRow,
                            mapping.SourceColumn))
                    .ToList();

                if (values.All(IsBlank))
                {
                    continue;
                }

                foreach (var mapping in bestMappings)
                {
                    var value = sourceSheet.GetValue(
                        sourceRow,
                        mapping.SourceColumn);

                    if (IsBlank(value))
                    {
                        continue;
                    }

                    var targetCell = bestTarget.Sheet.GetCell(
                        targetRow,
                        mapping.TargetColumn);

                    if (!string.IsNullOrWhiteSpace(
                        targetCell?.Formula))
                    {
                        continue;
                    }

                    result.Patches.Add(new CellPatch
                    {
                        Sheet = bestTarget.Sheet.Name,
                        Cell = XlsxAddress.ToCellReference(
                            targetRow,
                            mapping.TargetColumn),
                        Value = value
                    });
                }

                targetRow++;
                rowsWritten++;
            }

            if (rowsWritten == 0)
            {
                continue;
            }

            mappedTargets.Add(bestTarget.Sheet.Name);
            mappedAny = true;
            result.Warnings.Add(
                $"A flat table from '{sourceSheet.Name}' was mapped into '{bestTarget.Sheet.Name}' using deterministic normalized-header matching.");
        }

        if (!mappedAny)
        {
            result.Warnings.Add(
                "No supported wide financial-statement pattern or sufficiently matching flat table was found.");
        }
    }

    private static void AddSourceLog(
        List<CellPatch> patches,
        TargetTable? table,
        string originalFileName,
        int sheetCount)
    {
        if (table is null)
        {
            return;
        }

        WriteRecord(patches, table, table.DataStartRow, new Dictionary<string, object?>
        {
            ["Source_ID"] = "SRC-002",
            ["File / Source Name"] = originalFileName,
            ["Source Type"] = "Uploaded Excel workbook",
            ["Version / Date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Priority"] = "Primary",
            ["Used For"] = "Runtime source profiling, financial standardization and readiness review",
            ["Review Status"] = "Deterministic mapping complete; review required",
            ["Owner"] = "Organization / IREI",
            ["Notes"] = $"{sheetCount:N0} sheet(s) inspected. Source tabs and columns were discovered at runtime.",
            ["Supersedes / Related"] = ""
        });
    }

    private static void AddOutputMetadata(
        List<CellPatch> patches,
        WorkbookSnapshot template,
        string organizationName,
        string originalFileName,
        string reportingPeriod)
    {
        var output = template.Sheets.Values.FirstOrDefault(sheet =>
            sheet.Cells.Values.Any(cell =>
                string.Equals(
                    CleanText(cell.Value),
                    "IREI Stage 1 Readiness Snapshot",
                    StringComparison.OrdinalIgnoreCase)));

        if (output is null)
        {
            return;
        }

        patches.AddRange(
        [
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "A2",
                Value = $"{organizationName} — deterministic source mapping; review required"
            },
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "C9",
                Value = organizationName
            },
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "C11",
                Value = string.IsNullOrWhiteSpace(reportingPeriod)
                    ? DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)
                    : reportingPeriod
            },
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "C12",
                Value = originalFileName
            },
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "C13",
                Value = "Deterministic runtime mapping; organizational review required"
            },
            new CellPatch
            {
                Sheet = output.Name,
                Cell = "C14",
                Value = "Existing + New Projects"
            }
        ]);
    }

    private static void WriteWarnings(
        List<CellPatch> patches,
        TargetTable? table,
        IReadOnlyCollection<string> warnings)
    {
        if (table is null)
        {
            return;
        }

        var row = table.DataStartRow;
        var number = 1;

        foreach (var warning in warnings
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(30))
        {
            WriteRecord(patches, table, row, new Dictionary<string, object?>
            {
                ["Flag_ID"] = $"AUTO-{number:000}",
                ["Category"] = "Automated intake",
                ["Property_ID"] = "PORTFOLIO",
                ["Issue / Assumption"] = warning,
                ["Current Treatment"] = "Retain as unresolved until organizational review.",
                ["Severity"] = "Medium",
                ["Requires Confirmation?"] = "Yes",
                ["Owner"] = "IREI reviewer",
                ["Status"] = "Open",
                ["Reviewer Note"] = ""
            });

            row++;
            number++;
        }
    }

    private static TargetTable? FindTargetTable(
        WorkbookSnapshot template,
        params string[] requiredHeaders)
    {
        foreach (var sheet in template.Sheets.Values)
        {
            for (var row = 1; row <= Math.Min(sheet.MaxRow, 20); row++)
            {
                var headers = ReadHeaders(sheet, row);
                if (requiredHeaders.All(required =>
                    headers.Values.Any(header =>
                        Normalize(header) == Normalize(required))))
                {
                    return new TargetTable
                    {
                        Sheet = sheet,
                        HeaderRow = row,
                        DataStartRow = row + 1,
                        Columns = headers.ToDictionary(
                            pair => pair.Value,
                            pair => pair.Key,
                            StringComparer.OrdinalIgnoreCase)
                    };
                }
            }
        }

        return null;
    }

    private static IEnumerable<TargetTable> DiscoverTargetTables(
        WorkbookSnapshot template)
    {
        foreach (var sheet in template.Sheets.Values)
        {
            for (var row = 1; row <= Math.Min(sheet.MaxRow, 20); row++)
            {
                var headers = ReadHeaders(sheet, row);
                if (headers.Count < 3 ||
                    !headers.Values.Any(header =>
                        Normalize(header).EndsWith("id", StringComparison.Ordinal)))
                {
                    continue;
                }

                yield return new TargetTable
                {
                    Sheet = sheet,
                    HeaderRow = row,
                    DataStartRow = row + 1,
                    Columns = headers.ToDictionary(
                        pair => pair.Value,
                        pair => pair.Key,
                        StringComparer.OrdinalIgnoreCase)
                };
                break;
            }
        }
    }

    private static Dictionary<int, string> ReadHeaders(
        SheetSnapshot sheet,
        int row)
    {
        var result = new Dictionary<int, string>();

        for (var column = 1; column <= Math.Min(sheet.MaxColumn, 60); column++)
        {
            var text = CleanText(sheet.GetValue(row, column));
            if (!string.IsNullOrWhiteSpace(text))
            {
                result[column] = text;
            }
        }

        return result;
    }

    private static void WriteRecord(
        List<CellPatch> patches,
        TargetTable table,
        int row,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (var pair in values)
        {
            var column = table.Columns
                .Where(header =>
                    Normalize(header.Key) == Normalize(pair.Key))
                .Select(header => header.Value)
                .FirstOrDefault();

            if (column <= 0 || pair.Value is null)
            {
                continue;
            }

            var targetCell = table.Sheet.GetCell(row, column);
            if (!string.IsNullOrWhiteSpace(targetCell?.Formula))
            {
                continue;
            }

            patches.Add(new CellPatch
            {
                Sheet = table.Sheet.Name,
                Cell = XlsxAddress.ToCellReference(row, column),
                Value = pair.Value
            });
        }
    }

    private static UnitInference InferUnits(StatementProfile profile)
    {
        var candidates = new List<int>();

        for (var row = 1; row <= Math.Min(profile.YearRow, 12); row++)
        {
            for (var column = 1; column <= Math.Min(profile.Sheet.MaxColumn, profile.LabelColumn); column++)
            {
                var value = profile.Sheet.GetValue(row, column);

                if (value is string text)
                {
                    var formulaMatch = UnitFormulaRegex.Match(text);
                    if (formulaMatch.Success &&
                        int.TryParse(formulaMatch.Groups[1].Value, out var formulaUnits))
                    {
                        candidates.Add(formulaUnits);
                    }

                    if (text.Contains("unit", StringComparison.OrdinalIgnoreCase))
                    {
                        for (var nearbyColumn = 1;
                             nearbyColumn <= Math.Min(profile.Sheet.MaxColumn, 6);
                             nearbyColumn++)
                        {
                            var nearby = ToInt(profile.Sheet.GetValue(row, nearbyColumn));
                            if (nearby is > 0 and <= 5000)
                            {
                                candidates.Add(nearby);
                            }
                        }
                    }
                }
            }
        }

        for (var row = Math.Max(1, profile.YearRow - 1);
             row <= profile.YearRow;
             row++)
        {
            for (var column = 1; column < profile.LabelColumn; column++)
            {
                var candidate = ToInt(profile.Sheet.GetValue(row, column));
                if (candidate is > 0 and <= 5000)
                {
                    candidates.Add(candidate);
                }
            }
        }

        candidates = candidates
            .Where(value => value is > 0 and <= 5000)
            .Distinct()
            .ToList();

        return new UnitInference
        {
            Primary = candidates.ElementAtOrDefault(0),
            Alternate = candidates.ElementAtOrDefault(1),
            Conflict = candidates.Count > 1,
            Note = candidates.Count switch
            {
                0 => "Unit count not identified on the property statement.",
                1 => "Unit count inferred from the property statement.",
                _ => "Multiple possible unit counts identified; confirm the correct value."
            }
        };
    }

    private static YearColumn? FindFirstPositiveRevenueYear(
        StatementProfile profile)
    {
        var revenueRows = new List<int>();

        for (var row = profile.YearRow + 1; row <= profile.Sheet.MaxRow; row++)
        {
            var label = CleanText(profile.Sheet.GetValue(row, profile.LabelColumn));
            if (label.Contains("Rents Received", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Rental Revenue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "Rent Revenue", StringComparison.OrdinalIgnoreCase))
            {
                revenueRows.Add(row);
            }
        }

        if (revenueRows.Count == 0)
        {
            return null;
        }

        foreach (var year in profile.Years.OrderBy(item => item.EndYear))
        {
            var total = revenueRows
                .Select(row => ToDecimalOrNull(profile.Sheet.GetValue(row, year.Column)))
                .Where(value => value is not null)
                .Sum(value => value!.Value);

            if (total > 0)
            {
                return year;
            }
        }

        return null;
    }

    private static bool IsSectionHeading(
        string label,
        decimal? amount)
    {
        if (amount is not null)
        {
            return false;
        }

        var letters = label.Where(char.IsLetter).ToArray();
        var uppercase = letters.Length > 0 &&
                        letters.All(character =>
                            !char.IsLetter(character) ||
                            char.IsUpper(character));

        return uppercase &&
               (RevenueRegex.IsMatch(label) ||
                ExpenseRegex.IsMatch(label) ||
                label.Contains("OPERATING", StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferSection(string label)
    {
        if (label.Contains("REVENUE", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("INCOME", StringComparison.OrdinalIgnoreCase))
        {
            return "Revenue";
        }

        if (label.Contains("EXPENSE", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("MAINTENANCE", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("UTILIT", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return "Operating Expense";
        }

        return null;
    }

    private static string? InferCategory(
        string label,
        string? currentSection)
    {
        if (RevenueRegex.IsMatch(label))
        {
            return "Revenue";
        }

        if (ExpenseRegex.IsMatch(label))
        {
            return "Operating Expense";
        }

        return currentSection;
    }

    private static bool TryParseYear(
        object? value,
        out int endYear,
        out string label)
    {
        endYear = 0;
        label = CleanText(value);

        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var range = YearRangeRegex.Match(label);
        if (range.Success)
        {
            var start = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
            var endText = range.Groups[2].Value;
            var end = endText.Length == 4
                ? int.Parse(endText, CultureInfo.InvariantCulture)
                : start / 100 * 100 +
                  int.Parse(endText, CultureInfo.InvariantCulture);

            if (end < start)
            {
                end += 100;
            }

            endYear = end;
            return true;
        }

        if (SingleYearRegex.IsMatch(label))
        {
            endYear = int.Parse(label, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string ParseCity(string title)
    {
        var parts = title.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[^1] : "";
    }

    private static string FindTextAbove(
        SheetSnapshot sheet,
        int row,
        int column)
    {
        for (var candidateRow = row - 1; candidateRow >= 1; candidateRow--)
        {
            var text = CleanText(sheet.GetValue(candidateRow, column));
            if (!string.IsNullOrWhiteSpace(text) &&
                !text.Contains("annual payment", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return "";
    }

    private static string FindNearbyText(
        SheetSnapshot sheet,
        int startRow,
        int column,
        int distance)
    {
        for (var row = Math.Max(1, startRow - distance);
             row <= Math.Min(sheet.MaxRow, startRow + distance);
             row++)
        {
            var text = CleanText(sheet.GetValue(row, column));
            if (text.Contains("term", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("amort", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("%", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return "";
    }

    private static decimal FindNearbyNumber(
        SheetSnapshot sheet,
        int startRow,
        int column,
        int distance)
    {
        for (var row = startRow;
             row <= Math.Min(sheet.MaxRow, startRow + distance);
             row++)
        {
            var number = ToDecimalOrNull(sheet.GetValue(row, column));
            if (number is > 0)
            {
                return number.Value;
            }
        }

        return 0m;
    }

    private static decimal FindFirstAnnualPayment(
        SheetSnapshot sheet,
        int startRow,
        int loanColumn)
    {
        for (var row = startRow; row <= sheet.MaxRow; row++)
        {
            var dateText = CleanText(sheet.GetValue(row, 1));
            if (!Regex.IsMatch(dateText, @"\b(19|20)?\d{2}\b"))
            {
                continue;
            }

            var principal = ToDecimalOrNull(sheet.GetValue(row, loanColumn + 1)) ?? 0m;
            var interest = ToDecimalOrNull(sheet.GetValue(row, loanColumn + 2)) ?? 0m;
            if (principal != 0 || interest != 0)
            {
                return Math.Abs(principal) + Math.Abs(interest);
            }
        }

        return 0m;
    }

    private static decimal? ParsePercent(string text)
    {
        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        return match.Success &&
               decimal.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out var value)
            ? value / 100m
            : null;
    }

    private static int? ParseYears(
        string text,
        string marker)
    {
        var regex = marker == "term"
            ? new Regex(@"(\d+)\s*(?:yr|year)s?\s*term", RegexOptions.IgnoreCase)
            : new Regex(@"(\d+)\s*(?:yr|year)s?.{0,15}amort", RegexOptions.IgnoreCase);

        var match = regex.Match(text);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out var value)
            ? value
            : null;
    }

    private static int ToInt(object? value)
    {
        var number = ToDecimalOrNull(value);
        return number is null ? 0 : (int)Math.Round(number.Value);
    }

    private static decimal? ToDecimalOrNull(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        if (value is int integerValue)
        {
            return integerValue;
        }

        if (value is long longValue)
        {
            return longValue;
        }

        if (value is double doubleValue)
        {
            return (decimal)doubleValue;
        }

        return decimal.TryParse(
            value.ToString(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static string CleanText(object? value) =>
        value?.ToString()?.Trim() ?? "";

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "");

    private static bool IsBlank(object? value) =>
        value is null || string.IsNullOrWhiteSpace(value.ToString());

    private sealed class TargetTable
    {
        public required SheetSnapshot Sheet { get; init; }
        public required int HeaderRow { get; init; }
        public required int DataStartRow { get; init; }
        public required Dictionary<string, int> Columns { get; init; }
    }

    private sealed class StatementProfile
    {
        public required SheetSnapshot Sheet { get; init; }
        public required int YearRow { get; init; }
        public required List<YearColumn> Years { get; init; }
        public required int LabelColumn { get; init; }
        public required string Title { get; init; }
        public required bool IsAggregate { get; init; }
        public required int FinancialHitCount { get; init; }
        public required int TotalRowCount { get; init; }
        public required int UnitRowCount { get; init; }
        public required int ErrorCount { get; init; }
    }

    private sealed record YearColumn(int Column, int EndYear, string Label);

    private sealed class UnitInference
    {
        public int Primary { get; init; }
        public int Alternate { get; init; }
        public bool Conflict { get; init; }
        public string Note { get; init; } = "";
    }

    private sealed record PropertyInference(
        string PropertyId,
        string PropertyName,
        int PrimaryUnits,
        string Status);

    private sealed class DebtInference
    {
        public string PropertyId { get; init; } = "";
        public string PropertyName { get; init; } = "";
        public string DebtType { get; init; } = "";
        public string Lender { get; init; } = "";
        public decimal Principal { get; init; }
        public decimal? InterestRate { get; init; }
        public int? TermYears { get; init; }
        public int? AmortizationYears { get; init; }
        public decimal AnnualDebtService { get; init; }
        public string Note { get; init; } = "";
    }
}

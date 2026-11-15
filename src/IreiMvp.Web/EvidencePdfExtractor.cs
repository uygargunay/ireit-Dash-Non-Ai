using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace IreiMvp.Web;
public sealed record PdfFactProposal(string Code, decimal Value, string Period, string Locator, string Label);
public sealed record PdfTextPage(int Page, string Text);
public sealed record PdfExtraction(PdfTextPage[] Pages, PdfFactProposal[] Facts, string[] Warnings);
public static class EvidencePdfExtractor
{
    public static PdfExtraction Read(byte[] bytes)
    {
        try
        {
            using var pdf = PdfDocument.Open(bytes);
            if (pdf.NumberOfPages > 150) throw new InvalidDataException("PDF exceeds 150 pages. Split it into smaller documents.");
            var pages = new List<PdfTextPage>(); var facts = new List<PdfFactProposal>(); var warnings = new List<string>(); var chars = 0;
            foreach (var page in pdf.GetPages())
            {
                var lines = page.GetWords().GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4d)).OrderByDescending(g => g.Key)
                    .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToArray()).ToArray();
                var text = string.Join('\n', lines.Select(g => string.Join(' ', g.Select(w => w.Text)))); chars += text.Length;
                if (chars > 500000) throw new InvalidDataException("PDF text exceeds extraction capacity. Split this document.");
                pages.Add(new(page.Number, text));
                if (text.Trim().Length < 30) { warnings.Add($"Page {page.Number}: little or no readable text. OCR or a text-based original is needed; no numbers were invented."); continue; }
                var headers = new List<(double X, string Period)>();
                for (var line = 0; line < lines.Length; line++)
                {
                    var words = lines[line]; var valueText = string.Join(' ', words.Select(w => w.Text));
                    // A year header is carried only within this page, never guessed from the filename.
                    var years = words.Where(w => Regex.IsMatch(w.Text, @"^(?:FY)?20\d{2}(?:[-–/]20\d{2})?$", RegexOptions.IgnoreCase)).ToArray();
                    if (years.Length > 0 && !Regex.IsMatch(valueText, @"\b(total|income|expense|reserve|cash|payment)\b", RegexOptions.IgnoreCase))
                    { headers = years.Select(w => (w.BoundingBox.Left, w.Text)).ToList(); continue; }
                    var firstAmount = Array.FindIndex(words, w => Regex.IsMatch(w.Text, @"^(?:\(?-?\$?\d[\d,]*(?:\.\d{1,2})?\)?|—|-)$"));
                    if (firstAmount <= 0) continue;
                    var label = string.Join(' ', words.Take(firstAmount).Select(w => w.Text)).TrimEnd(' ', '$');
                    var code = IntakeExtractor.Suggest(label); if (code.Length == 0) continue;
                    var amounts = words.Skip(firstAmount).Where(w => Regex.IsMatch(w.Text, @"^\(?-?\$?\d[\d,]*(?:\.\d{1,2})?\)?$"));
                    foreach (var word in amounts)
                    {
                        if (!decimal.TryParse(word.Text.Replace("$", ""), NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out var number)) continue;
                        var period = headers.Count > 0 ? headers.OrderBy(h => Math.Abs(h.X - word.BoundingBox.Left)).First().Period : "Unconfirmed";
                        facts.Add(new(code, number, period, $"page {page.Number}, line {line + 1}, x {word.BoundingBox.Left:0.0}", label));
                        if (facts.Count > 10000) throw new InvalidDataException("Too many PDF candidates. Split the file for review.");
                    }
                }
            }
            warnings.Add("Text extraction is not verification. Check table alignment, property, period, currency, scale and reporting basis before use.");
            if (pages.Any(p => Regex.IsMatch(p.Text, @"thousands|millions|\$\s*000", RegexOptions.IgnoreCase))) warnings.Add("The PDF mentions thousands or millions. Values retain their printed scale; confirm and normalize before verification.");
            if (facts.Count == 0) warnings.Add("No supported financial labels were recognized. Read the extracted page text and enter the relevant facts; a scanned page requires OCR.");
            return new(pages.ToArray(), facts.ToArray(), warnings.ToArray());
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException && e is not StackOverflowException)
        { throw new InvalidDataException("PDF text could not be extracted. Supply an unlocked text PDF, split the document, or use a reviewed OCR copy."); }
    }
}

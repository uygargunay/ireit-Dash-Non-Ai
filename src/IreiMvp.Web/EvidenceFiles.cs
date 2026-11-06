using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;
public sealed class EvidenceFiles(IOptions<EvidenceOptions> options)
{
    public static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase) {
        [".pdf"] = "application/pdf", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [".csv"] = "text/csv",
        [".xls"] = "application/vnd.ms-excel", [".doc"] = "application/msword" };
    public async Task<(string Processing, string Message, string Mime)> Inspect(byte[] bytes, string name, CancellationToken ct)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!Types.TryGetValue(extension, out var mime)) return ("Rejected", "Use PDF, XLS/XLSX, CSV or DOC/DOCX.", "application/octet-stream");
        var text = "";
        try
        {
            if (extension is ".xlsx" or ".docx")
            {
                using var memory = new MemoryStream(bytes); using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
                if (zip.Entries.Count > 5000 || zip.Entries.Sum(e => e.Length) > 100 * 1024 * 1024) return ("Rejected", "Expanded document exceeds safe limits. Split the file or contact support.", mime);
                if (zip.GetEntry(extension == ".xlsx" ? "xl/workbook.xml" : "word/document.xml") is null || zip.GetEntry("[Content_Types].xml") is null)
                    return ("Rejected", "File content does not match its extension. Save a new copy in a supported format.", mime);
                if (zip.Entries.Any(e => e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))) return ("Quarantined", "Macro content requires a clean converted copy.", mime);
                var builder = new StringBuilder();
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                { using var reader = new StreamReader(entry.Open()); builder.Append(await reader.ReadToEndAsync(ct)); }
                text = builder.ToString();
            }
            else if (extension == ".pdf")
            { if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-") return ("Rejected", "File signature does not match PDF.", mime);
                text = Encoding.Latin1.GetString(bytes); if (text.Contains("/Encrypt", StringComparison.Ordinal)) return ("Quarantined", "Password-protected PDF needs an authorized unencrypted copy.", mime); }
            else if (extension is ".xls" or ".doc")
            { if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 })) return ("Rejected", "File signature does not match legacy Office. Convert to XLSX/DOCX.", mime);
                text = Encoding.Unicode.GetString(bytes); if (text.Contains("EncryptedPackage", StringComparison.OrdinalIgnoreCase)) return ("Quarantined", "Encrypted Office file needs a reviewed conversion.", mime); }
            else
            { text = new UTF8Encoding(false, true).GetString(bytes); if (text.Contains('\0')) return ("Rejected", "CSV must be UTF-8 text. Export a new copy.", mime); }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or DecoderFallbackException)
        { return ("Rejected", "The document could not be read. Save or convert a new copy.", mime); }
        var marker = text.ToUpperInvariant();
        if (marker.Contains("EICAR-STANDARD-ANTIVIRUS-TEST-FILE")) return ("Quarantined", "Security screening flagged this file. Contact support or provide a clean copy.", mime);
        if (new[] { "TENANT NAME", "HEALTH RECORD", "CASE NOTES", "PERSONAL INCOME", "SOCIAL INSURANCE NUMBER" }.Any(marker.Contains))
            return ("Quarantined", "Potential unnecessary personal information. Provide a redacted aggregate copy and contact support.", mime);
        if (options.Value.Prototype) return ("Accepted", "Synthetic-data prototype only; production malware clearance has not been established.", mime);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, options.Value.ScannerPort, timeout.Token);
            await using var stream = client.GetStream(); await stream.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), timeout.Token);
            var length = new byte[4];
            for (var offset = 0; offset < bytes.Length; offset += 65536)
            { var count = Math.Min(65536, bytes.Length - offset); BinaryPrimitives.WriteInt32BigEndian(length, count); await stream.WriteAsync(length, timeout.Token); await stream.WriteAsync(bytes.AsMemory(offset, count), timeout.Token); }
            await stream.WriteAsync(new byte[4], timeout.Token);
            var response = new byte[1024]; var used = 0;
            while (used < response.Length)
            { var read = await stream.ReadAsync(response.AsMemory(used), timeout.Token); if (read == 0) break; used += read; if (response.AsSpan(0, used).Contains((byte)0)) break; }
            return Encoding.ASCII.GetString(response, 0, used).TrimEnd('\0', '\r', '\n') == "stream: OK" ? ("Accepted", "Received; factual content remains unverified.", mime) : ("Quarantined", "Security screening did not clear the file. Contact support.", mime);
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        { return ("Quarantined", "Scanning is unavailable. File remains isolated; retry screening or use approved client-hosted sharing.", mime); }
    }
}

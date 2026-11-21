using System.Text.Json;

namespace IreiMvp.Web;

public sealed record BoardLocation(string PropertyId, string Name, string Address, decimal Latitude, decimal Longitude,
    string Source, string SourceDate, string State);
public sealed record BoardOrganizationStatement(string Vision, string Mission, string Source);
public sealed class BoardLocationCatalog
{
    public string Vision { get; set; } = "";
    public string Mission { get; set; } = "";
    public string StatementSource { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string CheckedDate { get; set; } = "";
    public List<BoardCatalogLocation> Locations { get; set; } = [];
}
public sealed class BoardCatalogLocation
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string[] Aliases { get; set; } = [];
}

// Public location observations do not amend financial/property/evidence records.
public static class BoardLocations
{
    private static readonly Lazy<BoardLocationCatalog[]> Catalogs = new(() =>
    {
        var assembly = typeof(BoardLocations).Assembly;
        return assembly.GetManifestResourceNames().Where(n => n.Contains(".Data.LocationRegisters.") && n.EndsWith(".json"))
            .Select(n => { using var stream = assembly.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<BoardLocationCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; }).ToArray();
    });

    public static BoardOrganizationStatement? StatementFor(IEnumerable<BoardLocation> locations)
    {
        var sources = locations.GroupBy(l => l.Source).Where(g => g.Select(l => l.PropertyId).Distinct().Count() >= 3)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var statements = Catalogs.Value.Where(c => sources.Contains(c.Source) && c.Vision.Length > 0 && c.Mission.Length > 0)
            .Select(c => new BoardOrganizationStatement(c.Vision, c.Mission, c.StatementSource)).Distinct().ToArray();
        return statements.Length == 1 ? statements[0] : null;
    }

    public static BoardLocation[] Match(IEnumerable<V4Row> propertyRows, IEnumerable<BoardLocationCatalog>? catalogs = null)
    {
        var properties = propertyRows.DistinctBy(p => p.Id).ToArray();
        var result = new List<BoardLocation>();
        foreach (var catalog in catalogs ?? Catalogs.Value)
        {
            var matches = new List<(V4Row Property, BoardCatalogLocation Location, bool AddressMatch)>();
            foreach (var property in properties)
            {
                var name = BoardWorkbookImporter.Norm(property.Text("Property_Name"));
                var address = BoardWorkbookImporter.Norm(property.Text("Address"));
                var candidates = catalog.Locations.Where(l =>
                    name.Length > 0 && new[] { l.Name }.Concat(l.Aliases).Any(a => BoardWorkbookImporter.Norm(a) == name) ||
                    address.Length > 0 && BoardWorkbookImporter.Norm(l.Address) == address).ToArray();
                if (candidates.Length != 1) continue;
                var location = candidates[0];
                var sameAddress = address.Length > 0 && BoardWorkbookImporter.Norm(location.Address) == address;
                if (address.Length > 0 && !sameAddress) continue;
                if (location.Latitude is < -85 or > 85 || location.Longitude is < -180 or > 180 || location.Address.Length == 0) continue;
                matches.Add((property, location, sameAddress));
            }
            // An isolated common building name cannot identify an organization's
            // public register. Require three distinct sites, or an exact address.
            var contextMatched = matches.Select(m => m.Location).Distinct().Count() >= 3;
            foreach (var match in matches.Where(m => contextMatched || m.AddressMatch))
                result.Add(new(match.Property.Id, match.Property.Text("Property_Name"), match.Location.Address,
                    match.Location.Latitude, match.Location.Longitude, catalog.Source, catalog.CheckedDate, "Public address · review match"));
        }
        // Conflicting public catalogs are withheld rather than silently selected.
        return result.GroupBy(r => r.PropertyId).Where(g => g.Select(x => (x.Address, x.Latitude, x.Longitude)).Distinct().Count() == 1)
            .Select(g => g.First()).ToArray();
    }
}

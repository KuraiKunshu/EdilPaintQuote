using System.Text;
using System.Text.RegularExpressions;

namespace EdilPaintPreventibiviGen.Services;

internal static class InterventionMessageTemplate
{
    private static readonly Regex FieldName = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private const int MaximumDepth = 16;

    public static string Render(string template, IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> rows)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(rows);
        var parts = new Parser(template).Parse();
        var rootFields = NormalizeFields(fields);
        var collections = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rows) collections[pair.Key] = pair.Value;
        ValidateCollections(parts, insideCollection: false);
        var output = new StringBuilder();
        AppendParts(parts, null);
        return output.ToString();

        void AppendParts(IReadOnlyList<Part> content, IReadOnlyDictionary<string, string>? rowFields)
        {
            foreach (var part in content)
            {
                switch (part)
                {
                    case TextPart text:
                        output.Append(text.Text);
                        break;
                    case FieldPart field:
                        output.Append(TryGetField(field.Name, rowFields, out var value) ? value : field.Token);
                        break;
                    case BlockPart block:
                        if (collections.TryGetValue(block.Name, out var collection))
                        {
                            foreach (var row in collection)
                                AppendParts(block.Content, NormalizeFields(row));
                        }
                        else if (TryGetField(block.Name, rowFields, out var condition) && !string.IsNullOrWhiteSpace(condition))
                            AppendParts(block.Content, rowFields);
                        break;
                }
            }
        }

        void ValidateCollections(IReadOnlyList<Part> content, bool insideCollection)
        {
            foreach (var block in content.OfType<BlockPart>())
            {
                bool isCollection = collections.ContainsKey(block.Name);
                if (insideCollection && isCollection)
                    throw new InvalidOperationException("Gli elenchi di materiali, lavorazioni e persone non possono essere inseriti uno dentro l’altro.");
                ValidateCollections(block.Content, insideCollection || isCollection);
            }
        }

        bool TryGetField(string name, IReadOnlyDictionary<string, string>? rowFields, out string value)
        {
            if (rowFields != null && rowFields.TryGetValue(name, out value!)) return true;
            return rootFields.TryGetValue(name, out value!);
        }
    }

    private static Dictionary<string, string> NormalizeFields(IReadOnlyDictionary<string, string> fields)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in fields) normalized[pair.Key] = pair.Value ?? string.Empty;
        return normalized;
    }

    private abstract record Part;
    private sealed record TextPart(string Text) : Part;
    private sealed record FieldPart(string Name, string Token) : Part;
    private sealed record BlockPart(string Name, IReadOnlyList<Part> Content) : Part;

    private sealed class Parser(string template)
    {
        private int _position;

        public IReadOnlyList<Part> Parse(string? expectedClosing = null, int depth = 0)
        {
            var parts = new List<Part>();
            var literal = new StringBuilder();
            while (_position < template.Length)
            {
                if (template[_position] != '{')
                {
                    literal.Append(template[_position++]);
                    continue;
                }

                bool blockTag = _position + 1 < template.Length && template[_position + 1] is '#' or '/';
                int closingBrace = template.IndexOf('}', _position + 1);
                if (closingBrace < 0)
                {
                    if (blockTag)
                        throw new InvalidOperationException("Un blocco del fac-simile non è completo. Chiudi il segnaposto con }.");
                    literal.Append(template.AsSpan(_position));
                    _position = template.Length;
                    break;
                }

                int nameStart = _position + (blockTag ? 2 : 1);
                string name = template[nameStart..closingBrace];
                if (!FieldName.IsMatch(name))
                {
                    if (blockTag)
                        throw new InvalidOperationException("Il nome di un blocco del fac-simile non è valido. Usa il nome di un segnaposto disponibile.");
                    literal.Append(template[_position++]);
                    continue;
                }

                if (literal.Length > 0)
                {
                    parts.Add(new TextPart(literal.ToString()));
                    literal.Clear();
                }
                string token = template[_position..(closingBrace + 1)];
                char marker = blockTag ? template[_position + 1] : '\0';
                _position = closingBrace + 1;
                if (marker == '/')
                {
                    if (!string.Equals(name, expectedClosing, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Il tag {token} non corrisponde al blocco aperto del fac-simile.");
                    return parts;
                }
                if (marker == '#')
                {
                    if (depth >= MaximumDepth)
                        throw new InvalidOperationException("Il fac-simile contiene troppi blocchi uno dentro l’altro. Il massimo è 16 livelli.");
                    parts.Add(new BlockPart(name, Parse(name, depth + 1)));
                }
                else
                    parts.Add(new FieldPart(name, token));
            }

            if (expectedClosing != null)
                throw new InvalidOperationException($"Manca il tag di chiusura {{/{expectedClosing}}} nel fac-simile.");
            if (literal.Length > 0) parts.Add(new TextPart(literal.ToString()));
            return parts;
        }
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace StockData.Application.Domain;

/// <summary>
/// A validated, normalised ticker symbol (e.g. <c>TSLA</c>, <c>^GSPC</c>, <c>BRK-B</c>, <c>VOD.L</c>, <c>EURUSD=X</c>).
/// </summary>
/// <remarks>
/// Normalisation trims whitespace and upper-cases. The character set covers equities, share
/// classes (<c>-</c>), exchange suffixes (<c>.</c>), indices (<c>^</c>) and FX/futures (<c>=</c>);
/// anything else is rejected before it reaches an upstream provider. At least one letter or digit
/// is required: punctuation-only input (<c>.</c>, <c>..</c>, <c>^</c>) is never a ticker, and a
/// <c>..</c> path segment would make the upstream URL resolve to a different path.
/// </remarks>
public sealed partial record StockSymbol
{
    public const int MaxLength = 14;

    private StockSymbol(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? input, [NotNullWhen(true)] out StockSymbol? symbol)
    {
        symbol = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalised = input.Trim().ToUpperInvariant();
        if (normalised.Length > MaxLength || !AllowedCharacters().IsMatch(normalised))
        {
            return false;
        }

        symbol = new StockSymbol(normalised);
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^(?=.*[A-Z0-9])[A-Z0-9.\-^=]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharacters();
}

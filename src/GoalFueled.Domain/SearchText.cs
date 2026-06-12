using System.Globalization;
using System.Text;
using GoalFueled.Domain.Entities;

namespace GoalFueled.Domain;

/// <summary>
/// Text helpers for the ingredient finder: search comparisons are case-insensitive and
/// accent/diacritic-insensitive (so "jalapeno" matches "jalapeño" and "CREMA" matches "crema").
/// The same folding rules are mirrored client-side in the web app's ingredient picker.
/// </summary>
public static class SearchText
{
    /// <summary>
    /// Folds a string for matching: lower-cases it (invariant) and strips combining
    /// diacritical marks via a Unicode FormD decomposition.
    /// </summary>
    public static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// True when the ingredient matches a folded search term against its name, aliases,
    /// tags or non-English names.
    /// </summary>
    public static bool MatchesIngredient(Ingredient ingredient, string foldedTerm)
    {
        if (Fold(ingredient.Name).Contains(foldedTerm)) return true;
        if (ingredient.Aliases is not null &&
            ingredient.Aliases.Any(a => Fold(a).Contains(foldedTerm))) return true;
        if (ingredient.Tags is not null &&
            ingredient.Tags.Any(t => Fold(t).Contains(foldedTerm))) return true;
        if (ingredient.LocalizedNames is not null &&
            ingredient.LocalizedNames.Any(l => Fold(l.Name).Contains(foldedTerm))) return true;
        return false;
    }
}

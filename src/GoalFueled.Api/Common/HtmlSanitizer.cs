using System.Text;
using System.Text.RegularExpressions;

namespace GoalFueled.Api.Common;

/// <summary>
/// Conservative, allow-list HTML sanitizer used as defense-in-depth on save. The primary XSS defense
/// is DOMPurify on the client at render time; this strips the obviously dangerous constructs (script/
/// style/iframe-style elements, event handlers, and javascript:/data: URIs) so we never persist them.
/// It intentionally errs toward dropping anything not on the allow-list rather than trying to be a
/// full HTML parser — descriptions are simple formatted text, not arbitrary documents.
/// </summary>
public static partial class HtmlSanitizer
{
    // Tags we keep; everything else has its angle brackets stripped (content preserved as text).
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup",
        "ul", "ol", "li", "blockquote", "pre", "code", "span", "div",
        "h1", "h2", "h3", "h4", "h5", "h6", "a", "hr", "table", "thead", "tbody", "tr", "td", "th",
    };

    // Elements whose entire content must be removed (not just unwrapped) — they can execute or restyle.
    private static readonly string[] DangerousElements =
    {
        "script", "style", "iframe", "object", "embed", "noscript", "template", "svg", "math", "link", "meta",
    };

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    [GeneratedRegex("<\\s*([a-zA-Z0-9]+)((?:[^<>\"']|\"[^\"]*\"|'[^']*')*)>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)", RegexOptions.Singleline)]
    private static partial Regex AttrRegex();

    /// <summary>
    /// Returns a sanitized copy of <paramref name="html"/>, or the value unchanged when it is null/empty.
    /// Safe to run on plain text (no tags) — it is a no-op in that case.
    /// </summary>
    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        var working = html;

        // 1) Drop comments outright (can hide conditional/script payloads).
        working = CommentRegex().Replace(working, string.Empty);

        // 2) Remove dangerous elements together with their content.
        foreach (var element in DangerousElements)
        {
            // Paired <tag>...</tag>
            working = Regex.Replace(
                working, $"<\\s*{element}\\b[^>]*>.*?<\\s*/\\s*{element}\\s*>",
                string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // Any stray/self-closing/opening tag that remains.
            working = Regex.Replace(
                working, $"<\\s*/?\\s*{element}\\b[^>]*>",
                string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        // 3) Rebuild the remaining tags from an allow-list, sanitizing attributes; closing tags pass
        //    through when allowed. Any disallowed tag has its brackets stripped (text preserved).
        var sb = new StringBuilder(working.Length);
        var lastIndex = 0;

        foreach (Match closeOrOpen in Regex.Matches(working, "</?\\s*[a-zA-Z][^>]*>", RegexOptions.Singleline))
        {
            sb.Append(working, lastIndex, closeOrOpen.Index - lastIndex);
            lastIndex = closeOrOpen.Index + closeOrOpen.Length;
            sb.Append(SanitizeTag(closeOrOpen.Value));
        }
        sb.Append(working, lastIndex, working.Length - lastIndex);

        return sb.ToString();
    }

    /// <summary>Sanitizes a single tag token, returning the rebuilt tag or empty when disallowed.</summary>
    private static string SanitizeTag(string tag)
    {
        var isClosing = Regex.IsMatch(tag, "^<\\s*/", RegexOptions.Singleline);
        var nameMatch = Regex.Match(tag, "^<\\s*/?\\s*([a-zA-Z0-9]+)");
        if (!nameMatch.Success) return string.Empty;

        var name = nameMatch.Groups[1].Value.ToLowerInvariant();
        if (!AllowedTags.Contains(name)) return string.Empty;

        if (isClosing) return $"</{name}>";

        // Keep only a small set of safe attributes; drop everything else (notably on* handlers, style).
        var attrs = new StringBuilder();
        foreach (Match attr in AttrRegex().Matches(tag))
        {
            var attrName = attr.Groups[1].Value.ToLowerInvariant();
            var rawValue = attr.Groups[2].Value.Trim('"', '\'');

            if (attrName == "href" && name == "a")
            {
                if (IsSafeUrl(rawValue))
                    attrs.Append($" href=\"{EncodeAttr(rawValue)}\"");
            }
            // title is harmless and useful; allow it on any kept element.
            else if (attrName == "title")
            {
                attrs.Append($" title=\"{EncodeAttr(rawValue)}\"");
            }
            // Everything else (style, on*, class, id, src, etc.) is dropped.
        }

        return $"<{name}{attrs}>";
    }

    /// <summary>Allows http/https/mailto and relative URLs; rejects javascript:, data:, and friends.</summary>
    private static bool IsSafeUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0) return false;
        // Strip control/whitespace that can obfuscate a scheme (e.g. "java\tscript:").
        var collapsed = Regex.Replace(trimmed, "\\s+", string.Empty);
        if (collapsed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return false;
        if (collapsed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (collapsed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string EncodeAttr(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

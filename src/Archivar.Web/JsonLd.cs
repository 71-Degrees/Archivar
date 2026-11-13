/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;

namespace Archivar.Web;

/// <summary>
/// Renders a schema.org JSON-LD block (<c>&lt;script type="application/ld+json"&gt;</c>) for embedding
/// directly in Razor markup. JSON-LD property names like <c>@type</c>/<c>@context</c>/<c>@graph</c> aren't
/// valid C# identifiers, so callers build the payload as a <see cref="JsonNode"/> (a <see cref="JsonObject"/>
/// or <see cref="JsonArray"/>) rather than a POCO.
///
/// Returns a <see cref="MarkupString"/> so the script tag isn't HTML-encoded by Razor's default <c>@expr</c>
/// output - the JSON itself is still safely escaped by <see cref="JsonSerializer"/> either way, and any value
/// containing a literal "&lt;/" (which could otherwise prematurely close the surrounding &lt;script&gt; tag,
/// even though it's a syntactically ordinary JSON string byte) is escaped to "&lt;\/" first.
/// </summary>
public static class JsonLd
{
    public static MarkupString Script(JsonNode data)
    {
        var json = data.ToJsonString().Replace("</", "<\\/", StringComparison.Ordinal);
        return new MarkupString($"<script type=\"application/ld+json\">{json}</script>");
    }
}

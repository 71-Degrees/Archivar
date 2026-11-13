/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Archivar.Core.Operations;
using WebCore.Operations;

namespace Archivar.Web.Operations;

/// <summary>
/// Reads release-notes.json, the release history tools/Deploy generates from git and ships inside every build (a running site has no
/// git to ask). Loaded once; a build without the file just has no notes.
/// </summary>
public class ReleaseNotesService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lazy<ReleaseNotes?> _notes;

    public ReleaseNotesService(IWebHostEnvironment env, ILogger<ReleaseNotesService> logger)
    {
        _notes = new Lazy<ReleaseNotes?>(() =>
        {
            var path = Path.Combine(env.ContentRootPath, "release-notes.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ReleaseNotes>(File.ReadAllText(path), Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Could not read release-notes.json");
                return null;
            }
        });
    }

    public ReleaseNotes? Notes => _notes.Value;
}

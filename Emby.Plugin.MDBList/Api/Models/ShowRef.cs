using System.Text.Json.Serialization;

namespace Emby.Plugin.MDBList.Api.Models;

/// <summary>
/// The "show" object nested inside an episode reference.
/// </summary>
public class ShowRef
{
    /// <summary>
    /// Gets or sets the show's provider ids.
    /// </summary>
    [JsonPropertyName("ids")]
    public MediaIds? Ids { get; set; }
}

namespace CameraMaintainer.Core.Enums;

/// <summary>
/// Camera view direction analysis result.
/// Determined through edge detection, texture entropy, and histogram analysis.
/// </summary>
public enum DirectionStatus
{
    /// <summary>Not yet analyzed or insufficient data.</summary>
    Unknown = 0,

    /// <summary>Normal scene — sufficient edges, texture variation, and color spread.</summary>
    Normal = 1,

    /// <summary>Camera pointing at a uniform surface (wall, ceiling, floor).</summary>
    WallObstructed = 2,

    /// <summary>Partially blocked — e.g. narrow slot crossing or object covering part of view.</summary>
    PartiallyObstructed = 3,

    /// <summary>Night / low-light — distinct from obstruction. May still have valid scene.</summary>
    DarkScene = 4,

    /// <summary>Blinding light / lens flare / overexposed.</summary>
    Overexposed = 5
}

namespace SegaLauncher;

/// <summary>One installable source-code package.</summary>
/// <param name="Id">Folder-safe id, also the install subfolder name.</param>
/// <param name="Name">Shown in the picker.</param>
/// <param name="BlobFile">Encrypted blob shipped next to the launcher.</param>
/// <param name="Description">One-line blurb under the picker.</param>
public sealed record SourceItem(string Id, string Name, string BlobFile, string Description);

/// <summary>The list of source codes this launcher can install. Add entries here.</summary>
public static class Catalog
{
    public static readonly SourceItem[] Items =
    {
        new SourceItem(
            "slowroads",
            "Slow Roads (modded)",
            "game.enc",
            "Endless driving zen - modded build. Full credit: slowroads.io (Anslo)."),
        new SourceItem(
            "eaglercraftx",
            "EaglercraftX 1.8",
            "eaglercraftx.enc",
            "Minecraft in the browser — EaglercraftX 1.8 WASM build."),
    };
}

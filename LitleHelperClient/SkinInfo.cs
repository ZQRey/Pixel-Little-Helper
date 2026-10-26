namespace PixelHelper;

public sealed record SkinInfo(
    string Id,
    string DisplayName,
    string Author,
    string Version,
    SkinManifest Manifest);

public sealed class SkinChangedEventArgs : EventArgs
{
    public string PreviousSkinId { get; }
    public string NewSkinId { get; }
    public SkinManifest NewSkinManifest { get; }

    public SkinChangedEventArgs(string previousSkinId, string newSkinId, SkinManifest newSkinManifest)
    {
        PreviousSkinId = previousSkinId;
        NewSkinId = newSkinId;
        NewSkinManifest = newSkinManifest;
    }
}

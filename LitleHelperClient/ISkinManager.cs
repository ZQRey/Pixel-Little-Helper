using System.Windows.Media.Imaging;

namespace PixelHelper;

public interface ISkinManager
{
    SkinInfo CurrentSkin { get; }
    string CurrentSkinId { get; }
    IReadOnlyList<SkinInfo> GetAvailableSkins();
    BitmapSource GetFrame(PetState state, int frameIndex);
    SpriteFrame GetSpriteFrame(PetState state, int frameIndex);
    bool SetSkin(string skinId);
    event EventHandler<SkinChangedEventArgs>? SkinChanged;
}

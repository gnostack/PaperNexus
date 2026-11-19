namespace PaperNexus.Core;

// The easter eggs this device has, as its Secrets list shows them. The host supplies it: an
// egg the device cannot play (the phone has no startup screen with words) is left out, and a
// hint that names a place or an action only one device has is worded for this device. The
// ids are always the catalog's, so a discovery counts on every device.
public interface IDeviceEasterEggs
{
    public IReadOnlyList<EasterEggDefinition> Eggs { get; }
}

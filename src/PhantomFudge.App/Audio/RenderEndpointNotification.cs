using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace PhantomFudge.App.Audio;

internal sealed class RenderEndpointNotification(string deviceId, Action disconnected) : IMMNotificationClient
{
    private int _invalidated;

    public void OnDeviceStateChanged(string changedId, DeviceState newState)
    {
        if (newState != DeviceState.Active)
            Invalidate(changedId);
    }

    public void OnDeviceRemoved(string removedId) => Invalidate(removedId);
    public void OnDeviceAdded(string addedId) { }
    public void OnPropertyValueChanged(string changedId, PropertyKey key) { }

    // Default-device changes must never switch the explicitly selected render endpoint.
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }

    private void Invalidate(string changedId)
    {
        if (string.Equals(deviceId, changedId, StringComparison.Ordinal) &&
            Interlocked.CompareExchange(ref _invalidated, 1, 0) == 0)
        {
            // Core Audio notification callbacks must return promptly and must not unregister themselves.
            ThreadPool.QueueUserWorkItem(_ => disconnected());
        }
    }
}

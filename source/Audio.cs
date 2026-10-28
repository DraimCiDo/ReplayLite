using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ReplayLite
{
    sealed class AudioChoice
    {
        public string Id, Name;
        public override string ToString() { return Name; }
        internal static List<AudioChoice> Enumerate(bool system, string selected)
        {
            var result = new List<AudioChoice> { new AudioChoice { Id = "", Name = "Устройство по умолчанию" } };
            try
            {
                using (var devices = new MMDeviceEnumerator())
                    foreach (var device in devices.EnumerateAudioEndPoints(system ? DataFlow.Render : DataFlow.Capture, DeviceState.Active))
                        using (device) result.Add(new AudioChoice { Id = device.ID, Name = device.FriendlyName });
            }
            catch (Exception) { /* Keep default and selected identity when audio service is unavailable. */ }
            if (!string.IsNullOrEmpty(selected) && !result.Exists(c => c.Id == selected)) result.Add(new AudioChoice { Id = selected, Name = "Выбранное устройство недоступно" });
            return result;
        }
    }
    // A source can be replaced without closing the PCM pipe consumed by FFmpeg.
    internal interface IAudioSource : IDisposable
    {
        string Failure { get; }
        long CapturedBytes { get; }
        void Start();
        int Read(float[] values);
    }
    sealed class WasapiAudioSource : IAudioSource
    {
        readonly WasapiCapture capture;
        readonly ISampleProvider samples;
        volatile bool disposed;
        volatile string failure;
        long capturedBytes;
        public string Failure { get { return failure; } }
        public long CapturedBytes { get { return Interlocked.Read(ref capturedBytes); } }
        public WasapiAudioSource(bool system) : this(system, "") { }
        public WasapiAudioSource(bool system, string deviceId)
        {
            try
            {
                using (var devices = new MMDeviceEnumerator())
                using (var device = string.IsNullOrEmpty(deviceId) ? devices.GetDefaultAudioEndpoint(system ? DataFlow.Render : DataFlow.Capture, system ? Role.Multimedia : Role.Console) : devices.GetDevice(deviceId))
                {
                    if (device.State != DeviceState.Active || device.DataFlow != (system ? DataFlow.Render : DataFlow.Capture)) throw new InvalidOperationException("Выбранное аудиоустройство недоступно.");
                    capture = system ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
                }
                var buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromMilliseconds(500), DiscardOnBufferOverflow = true, ReadFully = true };
                ISampleProvider source = buffer.ToSampleProvider();
                if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
                if (source.WaveFormat.Channels != 2) throw new NotSupportedException("Для аудиоустройства нужен моно- или стереоформат.");
                if (source.WaveFormat.SampleRate != 48000) source = new WdlResamplingSampleProvider(source, 48000);
                samples = source;
                capture.DataAvailable += delegate(object sender, WaveInEventArgs e) { if (!disposed) { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); Interlocked.Add(ref capturedBytes, e.BytesRecorded); } };
                capture.RecordingStopped += delegate(object sender, StoppedEventArgs e) { if (!disposed) failure = e.Exception == null ? "Аудиоустройство остановлено." : e.Exception.Message; };
            }
            catch { if (capture != null) capture.Dispose(); throw; }
        }
        public void Start() { capture.StartRecording(); }
        public int Read(float[] values) { return samples.Read(values, 0, values.Length); }
        public void Dispose() { disposed = true; capture.Dispose(); }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class AudioDeviceChanges : IMMNotificationClient, IDisposable
    {
        readonly MMDeviceEnumerator devices;
        readonly bool system;
        readonly string selectedDevice;
        int version;
        public int Version { get { return Interlocked.CompareExchange(ref version, 0, 0); } }
        internal AudioDeviceChanges(bool system, bool register = true, string deviceId = "")
        {
            this.system = system; selectedDevice = deviceId; if (!register) return; devices = new MMDeviceEnumerator();
            try { Marshal.ThrowExceptionForHR(devices.RegisterEndpointNotificationCallback(this)); }
            catch { devices.Dispose(); throw; }
        }
        public static AudioDeviceChanges TryCreate(bool system, string deviceId = "")
        { try { return new AudioDeviceChanges(system, true, deviceId); } catch (Exception) { return null; } }
        void Changed() { Interlocked.Increment(ref version); }
        void DeviceChanged(string id) { if (string.IsNullOrEmpty(selectedDevice) || id == selectedDevice) Changed(); }
        public void OnDeviceStateChanged(string id, DeviceState state) { DeviceChanged(id); }
        public void OnDeviceAdded(string id) { DeviceChanged(id); }
        public void OnDeviceRemoved(string id) { DeviceChanged(id); }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
        { if (string.IsNullOrEmpty(selectedDevice) && flow == (system ? DataFlow.Render : DataFlow.Capture) && role == (system ? Role.Multimedia : Role.Console)) Changed(); }
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
        public void Dispose()
        { if (devices == null) return; try { devices.UnregisterEndpointNotificationCallback(this); } catch (Exception) { } devices.Dispose(); }
    }
}

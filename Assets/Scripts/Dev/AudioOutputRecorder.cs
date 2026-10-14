using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Dev tool (M53): put on the GameObject with the active AudioListener to capture the
/// final mixed audio output to a 16-bit WAV, e.g. to check cue playback for clicks/pops.
/// Call Begin(path) / End() (from code or the context menu). Does nothing unless started.
/// </summary>
public class AudioOutputRecorder : MonoBehaviour
{
    private readonly object _lock = new object();
    private System.Collections.Generic.List<float> _buf;
    private int _channels = 2;
    private string _path;

    public bool IsRecording { get { lock (_lock) return _buf != null; } }

    /// <summary>Seconds of audio captured so far (use as an event marker).</summary>
    public float SecondsRecorded
    {
        get { lock (_lock) return _buf == null ? 0f : _buf.Count / (float)Mathf.Max(1, _channels) / AudioSettings.outputSampleRate; }
    }

    public void Begin(string path)
    {
        lock (_lock) { _buf = new System.Collections.Generic.List<float>(48000 * 2 * 20); _path = path; }
    }

    public string End()
    {
        float[] data; int ch; string path;
        lock (_lock)
        {
            if (_buf == null) return null;
            data = _buf.ToArray(); ch = _channels; path = _path; _buf = null;
        }
        WriteWav(path, data, ch, AudioSettings.outputSampleRate);
        return path;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        lock (_lock)
        {
            if (_buf == null) return;
            _channels = channels;
            _buf.AddRange(data);
        }
    }

    private static void WriteWav(string path, float[] data, int channels, int rate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = data.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (float f in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
        }
    }
}

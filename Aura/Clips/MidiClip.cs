﻿using Aura.Automations;
using Aura.Tracks;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using MidiFile = Melanchall.DryWetMidi.Core.MidiFile;
using Melanchall.DryWetMidi.Common;

namespace Aura.Clips;

public class MidiClip : Clip
{
    private string _midiFilePath = string.Empty;

    /// <summary>
    /// Create a new midi clip from file path.
    /// </summary>
    /// <param name="filePath">The path of the midi file.</param>
    public MidiClip(string filePath)
    {
        Name = Path.GetFileNameWithoutExtension(filePath);
        _midiFilePath = filePath;
        MidiFile = MidiFile.Read(filePath);
    }

    /// <summary>
    /// Create a new midi clip from file path and parent track.
    /// </summary>
    /// <param name="filePath">The path of the midi file.</param>
    /// <param name="parentTrack">Track of this clip.</param>
    internal MidiClip(string filePath, Track? parentTrack)
    {
        Name = Path.GetFileNameWithoutExtension(filePath);
        _midiFilePath = filePath;
        MidiFile = MidiFile.Read(filePath);
        parentTrack.AddClip(this);
    }

    /// <summary>
    /// Create a new midi clip from midi data.
    /// </summary>
    /// <param name="midiFile">Midi data.</param>
    public MidiClip(MidiFile midiFile)
    {
        MidiFile = midiFile;
    }

    /// <summary>
    /// Create a new midi clip from midi data and parent track.
    /// </summary>
    /// <param name="midiFile">Midi data.</param>
    /// <param name="parentTrack">Track of this clip.</param>
    internal MidiClip(MidiFile midiFile, Track? parentTrack)
    {
        MidiFile = midiFile;
        parentTrack.AddClip(this);
    }

    /// <inheritdoc/>
    public override void Play()
    {
        if (!IsInTrack)
        {
            throw new Exception("Clip must be inside a track to be played.");
        }

        if (IsPlaying() || !Enabled)
            return;

        double playDuration = Duration;

        MidiPlayback = MidiFile.GetPlayback();
        MidiPlayback.TrackProgram = true;
        MidiPlayback.TrackNotes = true;
        MidiPlayback.TrackControlValue = true;
        MidiPlayback.TrackPitchValue = true;
        MidiPlayback.PlaybackStart = new MetricTimeSpan(TimeSpan.FromSeconds(StartMarker));
        if (EndMarker > 0)
        {
            MidiPlayback.PlaybackEnd = new MetricTimeSpan(TimeSpan.FromSeconds(EndMarker));
        }
       
        MidiPlayback.Finished += (sender, e) => {
            PlaybackStopWatch.Stop();
        };

        Track.FireClip(this);

        // Start playback timer
        PlaybackStopWatch.Restart();
        PlaybackStopWatch.SetTime(TimeSpan.FromSeconds(StartMarker));

        // Start automations
        StartAutomations();
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        MidiPlayback?.Stop();
        MidiPlayback?.Dispose();
        AutomationCTS?.Cancel();
        PlaybackStopWatch.Reset();
    }

    /// <inheritdoc/>
    protected override void Stop(bool resetTimer)
    {
        MidiPlayback?.Stop();
        MidiPlayback?.Dispose();
        AutomationCTS?.Cancel();

        if (resetTimer)
            PlaybackStopWatch.Reset();
        else
            PlaybackStopWatch.Stop();
    }

    /// <inheritdoc/>
    public override void Seek(double time)
    {
        if (!IsPlaying())
        {
            throw new InvalidOperationException("Cannot seek if clip is not playing.");
        }

        if (time < StartMarker)
        {
            throw new ArgumentOutOfRangeException(nameof(time), $"Seek time must be between {StartMarker:n2} and {GetDuration():n2}. (file duration)");
        }

        if (time >= EndMarker && EndMarker != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(time), $"Seek time must be between {StartMarker:n2} and {EndMarker:n2}. (EndMarker)");
        }

        if (time > GetDuration() && EndMarker == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(time), $"Seek time must be between {StartMarker:n2} and {GetDuration():n2}. (file duration)");
        }

        if (MidiPlayback != null)
        {
            MidiPlayback.MoveToTime(new MetricTimeSpan(TimeSpan.FromSeconds(time)));
            PlaybackStopWatch.SetTime(TimeSpan.FromSeconds(time));
        }
    }

    /// <inheritdoc/>
    protected override string GetFilePath()
    {
        return _midiFilePath;
    }

    /// <inheritdoc/>
    public override double GetDuration()
    {
        return MidiFile.GetDuration<MetricTimeSpan>().TotalSeconds;
    }

    /// <inheritdoc/>
    public override Clip Split(double seconds)
    {
        if (seconds <= 0 || (seconds >= EndMarker && EndMarker != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds),
                $"Cannot split clip out of its range. Split must be between {StartMarker:n2} and {EndMarker:n2}. (EndMarker)");
        }

        this.EndMarker = seconds;
        return new MidiClip(MidiFile.Clone(), this.Track)
        {
            StartMarker = seconds,

            // Copy settings
            Enabled = this.Enabled,
            Name = this.Name,
        }.CopyAutomationsFrom(this);
    }

    /// <inheritdoc/>
    public override (Clip middle, Clip right) SplitFromTo(double start, double end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end),
                $"Split end must be greater than split start.");
        }

        if (start <= StartMarker || (start >= EndMarker && EndMarker != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(start),
                $"Cannot split clip out of its range. Split start must be between {StartMarker:n2} and {EndMarker:n2}. (EndMarker)");
        }

        if (end >= EndMarker && EndMarker != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(end),
                $"Cannot split clip out of its range. Split end must be between {start:n2} and {EndMarker:n2}. (EndMarker)");
        }

        this.EndMarker = start;
        var clone = MidiFile.Clone();
        var clone2 = MidiFile.Clone();
        return new(
            new MidiClip(clone, this.Track) {
                StartMarker = start,
                EndMarker = end,

                // Copy settings
                Enabled = this.Enabled,
                Name = this.Name,
            }.CopyAutomationsFrom(this),
            new MidiClip(clone2, this.Track) {
                StartMarker = end,

                // Copy settings
                Enabled = this.Enabled,
                Name = this.Name,
            }.CopyAutomationsFrom(this)
        );
    }

    /// <inheritdoc/>
    public override Clip CutOut(double start, double end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end),
                $"Cut end must be greater than cut start.");
        }

        if (start <= StartMarker || (start >= EndMarker && EndMarker != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(start),
                $"Cannot cut clip out of its range. Cut start must be between {StartMarker:n2} and {EndMarker:n2}. (EndMarker)");
        }

        if (end >= EndMarker && EndMarker != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(end),
                $"Cannot cut clip out of its range. Cut end must be between {start:n2} and {EndMarker:n2}. (EndMarker)");
        }

        this.EndMarker = start;
        return new MidiClip(MidiFile.Clone(), this.Track)
        {
            StartMarker = end,

            // Copy settings
            Enabled = this.Enabled,
            Name = this.Name,
        }.CopyAutomationsFrom(this);
    }

    /// <inheritdoc/>
    public override void Reverse(string destDirectory)
    {
        throw new NotSupportedException("Reverse of midi clips is not supported.");
    }

    public override Clip Duplicate()
    {
        return new MidiClip(MidiFile.Clone(), this.Track)
        {
            Enabled = this.Enabled,
            EndMarker = this.EndMarker,
            FadeIn = this.FadeIn,
            FadeOut = this.FadeOut,
            Name = this.Name,
            Pan = this.Pan,
            Pitch = this.Pitch,
            Speed = this.Speed,
            StartMarker = this.StartMarker,
            Time = this.Time,
            Volume = this.Volume,
            _midiFilePath = this._midiFilePath,
        }.CopyAutomationsFrom(this);
    }

    /// <inheritdoc/>
    protected override void ApplyParameter(AutomationParameter parameter, float value)
    {
        MidiEvent? midiEvent;

        switch (parameter)
        {
            // Volume
            case AutomationParameter.Volume:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.ChannelVolume),
                    (SevenBitNumber)value);
                break;

            // Pan
            case AutomationParameter.Pan:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.Pan),
                    (SevenBitNumber)value);
                break;

            // Balance
            case AutomationParameter.Balance:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.Balance),
                    (SevenBitNumber)value);
                break;

            // Sustain pedal
            case AutomationParameter.SustainPedal:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.DamperPedal),
                    (SevenBitNumber)value);
                break;

            // Portamento
            case AutomationParameter.Portamento:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.Portamento),
                    (SevenBitNumber)value);
                break;

            // Sostenuto pedal
            case AutomationParameter.SostenutoPedal:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.Sostenuto),
                    (SevenBitNumber)value);
                break;

            // Soft pedal
            case AutomationParameter.SoftPedal:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.SoftPedal),
                    (SevenBitNumber)value);
                break;

            // Legato pedal
            case AutomationParameter.LegatoPedal:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.LegatoFootswitch),
                    (SevenBitNumber)value);
                break;

            // Modulation wheel
            case AutomationParameter.Modulation:
                midiEvent = new ControlChangeEvent(
                    ControlUtilities.AsSevenBitNumber(ControlName.Modulation), 
                    (SevenBitNumber)value);
                break;

            default:
                return;
        }

        if (midiEvent != null) 
        {
            Track?.PluginInstrument?.ReceiveMidiEvent(midiEvent);
        }
    }
}

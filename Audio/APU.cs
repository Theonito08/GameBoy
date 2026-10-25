using GameBoyEmu.Main;
using GameBoyEmu.Memory;
using NAudio.Wave;
using System;
using System.IO;

namespace GameBoyEmu.Audio
{
    public class APU : ISaveState
    {
        private const ushort NR52 = 0xFF26;

        private const ushort NR10 = 0xFF10;
        private const ushort NR11 = 0xFF11;
        private const ushort NR12 = 0xFF12;
        private const ushort NR13 = 0xFF13;
        private const ushort NR14 = 0xFF14;

        private const ushort NR21 = 0xFF16;
        private const ushort NR22 = 0xFF17;
        private const ushort NR23 = 0xFF18;
        private const ushort NR24 = 0xFF19;

        private const ushort NR30 = 0xFF1A;
        private const ushort NR31 = 0xFF1B;
        private const ushort NR32 = 0xFF1C;
        private const ushort NR33 = 0xFF1D;
        private const ushort NR34 = 0xFF1E;

        private const ushort NR41 = 0xFF20;
        private const ushort NR42 = 0xFF21;
        private const ushort NR43 = 0xFF22;
        private const ushort NR44 = 0xFF23;

        private double FrameSequencerCycles = 0;
        private int FrameSequencerStep = 0;

        private SquareChannel Channel1;
        private SquareChannel Channel2;
        private WaveChannel Channel3;

        private NoiseChannel Channel4;
        public MEMORY memory;

        private static int CPUClock = 4194304;
        private static int SampleRate = 44100;

        private byte[] AudioBuffer = new byte[4096];
        private int AudioBufferPos = 0;
        private double Cycles = 0;
        private double CyclesPerSample = (double)CPUClock / SampleRate;

        private BufferedWaveProvider waveprovider;
        private WaveOutEvent waveOut;

        public APU(MEMORY memoryIn)
        {
            memory = memoryIn;

            Channel1 = new SquareChannel(hasSweep: true);
            Channel2 = new SquareChannel(hasSweep: false);
            Channel3 = new WaveChannel();
            Channel4 = new NoiseChannel();

            waveprovider = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, 1));
            waveprovider.BufferDuration = TimeSpan.FromMilliseconds(100);
            waveprovider.DiscardOnBufferOverflow = true;

            waveOut = new WaveOutEvent();
            waveOut.DesiredLatency = 50;
            waveOut.NumberOfBuffers = 2;
            waveOut.Init(waveprovider);
            waveOut.Play();
        }
        public void Step(int cycles)
        {
            StepFrameSequencer(cycles);
            Cycles += cycles;
            while (Cycles >= CyclesPerSample)
            {
                Cycles -= CyclesPerSample;
                int sample = Channel1.Sample(SampleRate) + Channel2.Sample(SampleRate) + Channel3.Sample(SampleRate) + Channel4.Sample(SampleRate);
                if (sample > short.MaxValue) sample = short.MaxValue;
                if (sample < short.MinValue) sample = short.MinValue;
                short finalSample = (short)sample;
                AudioBuffer[AudioBufferPos++] = (byte)(finalSample & 0xFF);
                AudioBuffer[AudioBufferPos++] = (byte)((finalSample >> 8) & 0xFF);
                if (AudioBufferPos >= AudioBuffer.Length)
                {
                    waveprovider.AddSamples(AudioBuffer, 0, AudioBufferPos);
                    Console.WriteLine($"Buffered: {waveprovider.BufferedBytes}");
                    AudioBufferPos = 0;
                }
            }

        }
        public void StepFrameSequencer(int cycles)
        {
            FrameSequencerCycles += cycles;
            double FrameSequencerPeriod = CPUClock / 512.0;
            while (FrameSequencerCycles >= FrameSequencerPeriod)
            {
                FrameSequencerCycles -= FrameSequencerPeriod;
                if (FrameSequencerStep == 0 || FrameSequencerStep == 2 || FrameSequencerStep == 4 || FrameSequencerStep == 6)
                {
                    Channel1.ClockLength();
                    Channel2.ClockLength();
                    Channel3.ClockLength();
                    Channel4.ClockLength();
                }
                if (FrameSequencerStep == 2 || FrameSequencerStep == 6) Channel1.ClockSweep();
                if (FrameSequencerStep == 7)
                {
                    Channel1.ClockEnvelope();
                    Channel2.ClockEnvelope();
                    Channel4.ClockEnvelope();
                }
                FrameSequencerStep++;
                FrameSequencerStep &= 7;
            }
        }
        private bool ShouldExtraLengthClock()
        {
            return (FrameSequencerStep & 1) == 1;
        }
        public void WriteToSoundRegister(ushort address, byte value)
        {
            int lengthLoad;
            if (address == NR52)
            {
                if ((value & 0x80) == 0)
                {
                    // Power APU off
                    Channel1.Reset();
                    Channel2.Reset();
                    Channel3.Reset();
                    Channel4.Reset();

                    for (ushort i = 0xFF10; i <= 0xFF25; i++)
                    {
                        memory.ForceWriteByte(i, 0x00);
                    }
                    memory.ForceWriteByte(NR52, 0x00);
                    waveprovider.ClearBuffer();
                    AudioBufferPos = 0;
                    return;
                }
                memory.ForceWriteByte(NR52, 0x80);
                return;
            }
            if ((memory.RawReadByte(NR52) & 0x80) == 0)
            {
                return;
            }
            memory.ForceWriteByte(address, value);
            if (address >= 0xFF30 && address <= 0xFF3F)
            {
                Channel3.WriteWaveRAM(address - 0xFF30, value);
                memory.ForceWriteByte(address, value);
                return;
            }
            switch (address)
            {
                case NR10:
                    Channel1.SetSweep(value);
                    break;
                case NR11:
                    Channel1.SetDuty(value);
                    lengthLoad = value & 0x3F;
                    Channel1.SetLength(64 - lengthLoad);
                    break;
                case NR12:
                    Channel1.SetEnvelope(value);
                    if ((value & 0xF8) == 0) Channel1.Disable();
                    break;
                case NR13:
                    UpdateChannel1Frequency();
                    break;
                case NR14:
                    bool newLengthOn = (value & 0x40) != 0;
                    bool trigger = (value & 0x80) != 0;
                    Channel1.SetLengthEnabled(newLengthOn, ShouldExtraLengthClock());
                    UpdateChannel1Frequency();
                    if (trigger) Channel1.Trigger(ShouldExtraLengthClock());
                    break;
                case NR21:
                    Channel2.SetDuty(value);
                    lengthLoad = value & 0x3F;
                    Channel2.SetLength(64 - lengthLoad);
                    break;
                case NR22:
                    Channel2.SetEnvelope(value);
                    if ((value & 0xF8) == 0) Channel2.Disable();
                    break;
                case NR23:
                    UpdateChannel2Frequency();
                    break;
                case NR24:
                    newLengthOn = (value & 0x40) != 0;
                    trigger = (value & 0x80) != 0;
                    Channel2.SetLengthEnabled(newLengthOn, ShouldExtraLengthClock());
                    UpdateChannel2Frequency();
                    if (trigger) Channel2.Trigger(ShouldExtraLengthClock());
                    break;
                case NR30:
                    Channel3.SetDAC((value & 0x80) != 0);
                    break;
                case NR31:
                    Channel3.SetLength(value);
                    break;
                case NR32:
                    Channel3.SetVolume(value);
                    break;
                case NR33:
                    UpdateChannel3Frequency();
                    break;
                case NR34:
                    newLengthOn = (value & 0x40) != 0;
                    trigger = (value & 0x80) != 0;
                    Channel3.SetLengthEnabled(newLengthOn, ShouldExtraLengthClock());
                    UpdateChannel3Frequency();
                    if (trigger) Channel3.Trigger(ShouldExtraLengthClock());
                    break;
                case NR41:
                    lengthLoad = value & 0x3F;
                    Channel4.SetLength(64 - lengthLoad);
                    break;
                case NR42:
                    Channel4.SetEnvelope(value);
                    if ((value & 0xF8) == 0) Channel4.Disable();
                    break;
                case NR43:
                    Channel4.SetFrequency(value);
                    break;
                case NR44:
                    newLengthOn = (value & 0x40) != 0;
                    trigger = (value & 0x80) != 0;
                    Channel4.SetLengthEnabled(newLengthOn, ShouldExtraLengthClock());
                    if (trigger) Channel4.Trigger(ShouldExtraLengthClock());
                    break;
            }
        }
        public byte ReadFromSoundRegister(ushort address)
        {
            if (address >= 0xFF30 && address <= 0xFF3F)
            {
                return memory.RawReadByte(address);
            }

            byte value = memory.RawReadByte(address);

            switch (address)
            {
                // Channel 1
                case NR10:
                    return (byte)(value | 0x80);

                case NR11:
                    return (byte)(value | 0x3F);

                case NR12:
                    return value;

                case NR13:
                    return 0xFF;

                case NR14:
                    return (byte)(value | 0xBF);

                // Channel 2
                case NR21:
                    return (byte)(value | 0x3F);

                case NR22:
                    return value;

                case NR23:
                    return 0xFF;

                case NR24:
                    return (byte)(value | 0xBF);

                // Channel 3
                case 0xFF1A:// NR30
                    return (byte)(value | 0x7F);

                case 0xFF1B: // NR31
                    return 0xFF;

                case 0xFF1C: // NR32
                    return (byte)(value | 0x9F);

                case 0xFF1D: // NR33
                    return 0xFF;

                case 0xFF1E: // NR34
                    return (byte)(value | 0xBF);

                // Channel 4
                case 0xFF20: // NR41
                    return 0xFF;

                case 0xFF21: // NR42
                    return value;

                case 0xFF22: // NR43
                    return value;

                case 0xFF23: // NR44
                    return (byte)(value | 0xBF);

                // Output
                case 0xFF24: // NR50
                    return value;

                case 0xFF25: // NR51
                    return value;

                case NR52:
                    byte result = 0x70;
                    if ((memory.RawReadByte(0xFF26) & 0x80) != 0) result |= 0x80;
                    if (Channel1.IsOn()) result |= 0x01;
                    if (Channel2.IsOn()) result |= 0x02;
                    if (Channel3.IsOn()) result |= 0x04;
                    if (Channel4.IsOn()) result |= 0x08;
                    return result;

                // Unused audio registers
                case 0xFF15:
                case 0xFF1F:
                case 0xFF27:
                case 0xFF28:
                case 0xFF29:
                case 0xFF2A:
                case 0xFF2B:
                case 0xFF2C:
                case 0xFF2D:
                case 0xFF2E:
                case 0xFF2F:
                    return 0xFF;
            }

            return 0xFF;
        }
        private void UpdateChannel1Frequency()
        {
            byte periodLow = memory.RawReadByte(NR13);
            byte periodHigh = memory.RawReadByte(NR14);
            ushort period = (ushort)(periodLow | ((periodHigh & 0x07) << 8));
            Channel1.SetFrequency(period);
        }
        private void UpdateChannel2Frequency()
        {
            byte periodLow = memory.RawReadByte(NR23);
            byte periodHigh = memory.RawReadByte(NR24);
            ushort period = (ushort)(periodLow | ((periodHigh & 0x07) << 8));
            Channel2.SetFrequency(period);
        }
        private void UpdateChannel3Frequency()
        {
            byte periodLow = memory.RawReadByte(NR33);
            byte periodHigh = memory.RawReadByte(NR34);
            ushort period = (ushort)(periodLow | ((periodHigh & 0x07) << 8));
            Channel3.SetFrequency(period);
        }
        public void Save(BinaryWriter stateWriter)
        {
            stateWriter.Write(FrameSequencerCycles);
            stateWriter.Write(FrameSequencerStep);

            Channel1.Save(stateWriter);
            Channel2.Save(stateWriter);
            Channel3.Save(stateWriter);
            Channel4.Save(stateWriter);

            stateWriter.Write(Cycles);
            stateWriter.Write(AudioBufferPos);
        }
        public void Load(BinaryReader stateReader)
        {
            FrameSequencerCycles = stateReader.ReadDouble();
            FrameSequencerStep = stateReader.ReadInt32();

            Channel1.Load(stateReader);
            Channel2.Load(stateReader);
            Channel3.Load(stateReader);
            Channel4.Load(stateReader);

            Cycles = stateReader.ReadDouble();
            AudioBufferPos = stateReader.ReadInt32();
        }
    }
}

using System;
using System.IO;

namespace GameBoyEmu.Audio
{
    public class WaveChannel : AudioChannel
    {
        private double phase;
        private double frequency;
        private int periodValue;
        private int volumeCode;
        private byte[] waveRAM = new byte[16];

        public override void Reset()
        {
            base.Reset();
            phase = 0;
            frequency = 0;
            periodValue = 0;
            volumeCode = 0;
            Array.Clear(waveRAM, 0, waveRAM.Length);
        }
        public void SetLength(byte value)
        {
            Length = 256 - value;
            if (Length == 0) Length = 256;
        }

        public void SetVolume(byte value)
        {
            volumeCode = (value >> 5) & 0x03;
        }

        public void SetFrequency(ushort period)
        {
            periodValue = period;
            if (periodValue < 2048) frequency = 65536.0 / (2048 - periodValue);
        }

        public void SetLengthEnabled(bool enabled, bool extraLengthClock)
        {
            bool oldEnabled = LengthOn;
            LengthOn = enabled;
            if (!oldEnabled && enabled && extraLengthClock) ClockLength();
        }

        public void Trigger(bool extraLengthClock)
        {
            if(Length == 0)
            {
                Length = 256;
                if (LengthOn && extraLengthClock) Length--;
            }
            ChannelOn = true;
            phase = 0;
        }

        public void SetDAC(bool enabled)
        {
            if (!enabled) ChannelOn = false;
        }

        public void WriteWaveRAM(int index, byte value)
        {
            if (index < 0 || index >= 16) return;
            waveRAM[index] = value;
        }

        public byte ReadWaveRAM(int index)
        {
            if (index < 0 || index >= 16) return 0xFF;
            return waveRAM[index];
        }

        public override short Sample(int sampleRate)
        {
            if (!ChannelOn) return 0;
            if (volumeCode == 0) return 0;
            phase += frequency / sampleRate;
            phase -= Math.Floor(phase);
            int sampleIndex = (int)(phase * 32.0);
            byte waveByte = waveRAM[sampleIndex / 2];
            int sample;
            if ((sampleIndex & 1) == 0) sample = waveByte >> 4;
            else sample = waveByte & 0x0F;

            sample -= 8;
            switch (volumeCode)
            {
                case 1:
                    break;
                case 2:
                    sample >>= 1;
                    break;
                case 3:
                    sample >>= 2;
                    break;
            }
            return (short)(sample * 1000);
        }

        public override void Save(BinaryWriter writer)
        {
            writer.Write(phase);
            writer.Write(frequency);
            writer.Write(periodValue);
            writer.Write(volumeCode);
            for (int i = 0; i < 16; i++)
            {
                writer.Write(waveRAM[i]);
            }
            writer.Write(ChannelOn);
            writer.Write(Volume);
            writer.Write(Length);
            writer.Write(LengthOn);
            writer.Write(EnvPeriod);
            writer.Write(EnvTimer);
            writer.Write(EnvDirection);
        }

        public override void Load(BinaryReader reader)
        {
            phase = reader.ReadDouble();
            frequency = reader.ReadDouble();
            periodValue = reader.ReadInt32();
            volumeCode = reader.ReadInt32();
            for (int i = 0; i < 16; i++)
            {
                waveRAM[i] = reader.ReadByte();
            }
            ChannelOn = reader.ReadBoolean();
            Volume = reader.ReadInt32();
            Length = reader.ReadInt32();
            LengthOn = reader.ReadBoolean();
            EnvPeriod = reader.ReadInt32();
            EnvTimer = reader.ReadInt32();
            EnvDirection = reader.ReadInt32();
        }
    }
}

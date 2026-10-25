using System;
using System.IO;

namespace GameBoyEmu.Audio
{
    public class NoiseChannel : AudioChannel
    {
        private ushort lfsr;
        private double phase;
        private double frequency;
        private int divisor;
        private int clockShift;
        private bool sevenBitMode;

        public override void Reset()
        {
            base.Reset();
            lfsr = 0x7FFF;
            phase = 0;
            frequency = 0;
            divisor = 0;
            clockShift = 0;
            sevenBitMode = false;
        }

        public void SetFrequency(byte value)
        {
            divisor = value & 0x07;
            sevenBitMode = (value & 0x08) != 0;
            clockShift = (value >> 4) & 0x0F;
            int divisorValue;
            switch (divisor)
            {
                case 0:
                    divisorValue = 8;
                    break;
                case 1:
                    divisorValue = 16;
                    break;
                case 2:
                    divisorValue = 32;
                    break;
                case 3:
                    divisorValue = 48;
                    break;
                case 4:
                    divisorValue = 64;
                    break;
                case 5:
                    divisorValue = 80;
                    break;
                case 6:
                    divisorValue = 96;
                    break;
                case 7:
                    divisorValue = 112;
                    break;
                default:
                    divisorValue = 8;
                    break;
            }

            frequency = 524288.0 / divisorValue / Math.Pow(2, clockShift);
        }

        public void SetLength(int length)
        {
            Length = length;
        }
        public void SetLengthEnabled(bool enabled, bool extraLengthClock)
        {
            bool oldEnabled = LengthOn;
            LengthOn = enabled;
            if (!oldEnabled && enabled && extraLengthClock) ClockLength();
        }
        public void Trigger(bool extraLengthClock)
        {
            if (Length == 0)
            {
                Length = 64;
                if (LengthOn && extraLengthClock) Length--;
            }
            ChannelOn = true;
            lfsr = 0x7FFF;
            phase = 0;
        }

        private void ClockLFSR()
        {
            int bit = (lfsr & 0x01) ^ ((lfsr >> 1) & 0x01);
            lfsr >>= 1;
            lfsr |= (ushort)(bit << 14);
            if (sevenBitMode)
            {
                lfsr &= 0xFFBF;
                lfsr |= (ushort)(bit << 6);
            }
        }

        public override short Sample(int sampleRate)
        {
            if (!ChannelOn) return 0;
            phase += frequency / sampleRate;
            while (phase >= 1.0)
            {
                phase -= 1.0;
                ClockLFSR();
            }
            int output = (~lfsr) & 0x01;
            double volume = Volume / 15.0;
            return (short)(output * volume * 3000);
        }

        public override void Save(BinaryWriter writer)
        {
            writer.Write(lfsr);
            writer.Write(phase);
            writer.Write(frequency);
            writer.Write(divisor);
            writer.Write(clockShift);
            writer.Write(sevenBitMode);
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
            lfsr = reader.ReadUInt16();
            phase = reader.ReadDouble();
            frequency = reader.ReadDouble();
            divisor = reader.ReadInt32();
            clockShift = reader.ReadInt32();
            sevenBitMode = reader.ReadBoolean();

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

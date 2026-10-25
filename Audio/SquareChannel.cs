using System;
using System.IO;

namespace GameBoyEmu.Audio
{
    public class SquareChannel : AudioChannel
    {
        private double phase;
        private double frequency;
        private int periodValue;
        private int dutyCycle;

        //Exclusive to CH1 for sweep
        private bool hasSweep;
        private bool sweepEnabled;
        private int sweepPeriod;
        private int sweepTimer;
        private int sweepShift;
        private bool sweepNegate;
        private bool sweepNegateUsed;
        private int sweepShadow;

        public SquareChannel(bool hasSweep)
        {
            this.hasSweep = hasSweep;
        }
        public override void Reset()
        {
            base.Reset();
            phase = 0;
            frequency = 0;
            periodValue = 0;
            dutyCycle = 0;
            sweepEnabled = false;
            sweepPeriod = 0;
            sweepTimer = 0;
            sweepShift = 0;
            sweepNegate = false;
            sweepNegateUsed = false;
            sweepShadow = 0;
        }
        public void SetLength(int length)
        {
            Length = length;
        }
        public void SetDuty(byte value)
        {
            dutyCycle = (value >> 6) & 0x03;
        }
        public void SetFrequency(ushort period)
        {
            periodValue = period;

            if (periodValue < 2048)
            {
                frequency = 131072.0 / (2048 - periodValue);
            }
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
            phase = 0;
            if (hasSweep)
            {
                sweepNegateUsed = false;
                sweepShadow = periodValue;
                sweepTimer = sweepPeriod;
                if (sweepTimer == 0) sweepTimer = 8;
                sweepEnabled = sweepPeriod != 0 || sweepShift != 0;
                if (sweepShift != 0)
                {
                    int newPeriod = CalculateSweep();
                    if (newPeriod > 2047)
                    {
                        ChannelOn = false;
                        return;
                    }
                }
            }
        }
        private double GetDutyPercentage()
        {
            switch (dutyCycle)
            {
                case 0:
                    return 0.125;
                case 1:
                    return 0.25;
                case 2:
                    return 0.5;
                case 3:
                    return 0.75;
                default:
                    return 0.5;
            }
        }
        public override short Sample(int sampleRate)
        {
            if (!ChannelOn) return 0;
            phase += frequency / sampleRate;
            phase -= Math.Floor(phase);
            double dutyPercentage = GetDutyPercentage();
            int squareWave = phase < dutyPercentage ? 1 : -1;
            double volume = Volume / 15.0;
            return (short)(squareWave * volume * 3000);
        }

        public void SetSweep(byte value)
        {
            if (!hasSweep) return;
            bool oldNegate = sweepNegate;
            bool newNegate = (value & 0x08) != 0;
            if (oldNegate && !newNegate && sweepNegateUsed) ChannelOn = false;
            sweepShift = value & 0x07;
            sweepPeriod = (value >> 4) & 0x07;
            sweepNegate = newNegate;
        }

        private int CalculateSweep()
        {
            int change = sweepShadow >> sweepShift;
            if (sweepNegate)
            {
                sweepNegateUsed = true;
                return sweepShadow - change;
            }
            return sweepShadow + change;
        }

        public void ClockSweep()
        {
            if (!hasSweep || !sweepEnabled) return;
            sweepTimer--;
            if (sweepTimer > 0) return;
            sweepTimer = sweepPeriod;
            if (sweepTimer == 0) sweepTimer = 8;
            if (sweepPeriod == 0) return;
            if (sweepShift == 0) return;
            int newPeriod = CalculateSweep();
            if (newPeriod > 2047)
            {
                ChannelOn = false;
                return;
            }
            sweepShadow = newPeriod;
            SetFrequency((ushort)newPeriod);
            int overflowCheck = CalculateSweep();
            if (overflowCheck > 2047) ChannelOn = false;
        }
        public override void Save(BinaryWriter writer)
        {
            writer.Write(phase);
            writer.Write(frequency);
            writer.Write(periodValue);
            writer.Write(dutyCycle);
            writer.Write(hasSweep);
            writer.Write(sweepEnabled);
            writer.Write(sweepPeriod);
            writer.Write(sweepTimer);
            writer.Write(sweepShift);
            writer.Write(sweepNegate);
            writer.Write(sweepNegateUsed);
            writer.Write(sweepShadow);
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
            dutyCycle = reader.ReadInt32();
            hasSweep = reader.ReadBoolean();
            sweepEnabled = reader.ReadBoolean();
            sweepPeriod = reader.ReadInt32();
            sweepTimer = reader.ReadInt32();
            sweepShift = reader.ReadInt32();
            sweepNegate = reader.ReadBoolean();
            sweepNegateUsed = reader.ReadBoolean();
            sweepShadow = reader.ReadInt32();
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

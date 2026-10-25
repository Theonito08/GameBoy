using GameBoyEmu.Main;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameBoyEmu.Audio
{
    public abstract class AudioChannel : ISaveState
    {
        public bool ChannelOn;
        public int Volume;
        public int Length;
        public bool LengthOn;
        public int EnvPeriod;
        public int EnvTimer;
        public int EnvDirection;

        public virtual void Reset()
        {
            ChannelOn = false;
            Volume = 0;
            Length = 0;
            LengthOn = false;
            EnvPeriod = 0;
            EnvTimer = 0;
            EnvDirection = 0;
        }
        public abstract short Sample(int sampleRate);

        public void SetEnvelope(byte value)
        {
            Volume = (value >> 4) & 0x0F;
            EnvDirection = (value & 0x08) != 0 ? 1 : -1;
            EnvPeriod = value & 0x07;
            EnvTimer = EnvPeriod == 0 ? 8 : EnvPeriod;
            if ((value & 0xF8) == 0) ChannelOn = false;
        }
        public void Disable()
        {
            ChannelOn = false;
        }
        public bool IsOn()
        {
            return ChannelOn;
        }
        public void ClockLength()
        {
            if (!LengthOn) return;
            if ( Length > 0)
            {
                Length--;
                if (Length == 0) ChannelOn = false;
            }
        }
        public void ClockEnvelope()
        {
            if (EnvPeriod == 0) return;
            EnvTimer--;
            if(EnvTimer <= 0)
            {
                EnvTimer = EnvPeriod;
                int newVolume = Volume + EnvDirection;
                if (newVolume >= 0 && newVolume <= 15) Volume = newVolume;
            }
        }
        public abstract void Save(BinaryWriter writer);
        public abstract void Load(BinaryReader reader);
    }
}

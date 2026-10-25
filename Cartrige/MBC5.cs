using System.IO;

namespace GameBoyEmu.Cartrige
{
    public class MBC5 : IMBC
    {
        private byte[] ROM;
        private byte[] externalRAM;
        private int romBank = 1;
        private int ramBank = 0;
        private bool ramEnabled = false;

        public MBC5(byte[] ROM, byte[] externalRAM)
        {
            this.ROM = ROM;
            this.externalRAM = externalRAM;
        }
        public byte Read(ushort address)
        {
            if (address <= 0x7FFF) return ReadROM(address);
            if (address >= 0xA000 && address <= 0xBFFF) return ReadRAM(address);
            return 0xFF;
        }
        public void Write(ushort address, byte value)
        {
            if(address <= 0x7FFF)
            {
                WriteControl(address, value);
                return;
            }
            if (address >= 0xA000 && address <= 0xBFFF) WriteRAM(address, value);
        }
        private byte ReadROM(ushort address)
        {
            if(address <= 0x3FFF)
            {
                if (address < ROM.Length) return ROM[address];
                return 0xFF;
            }
            int romAddress = romBank * 0x4000 + (address - 0x4000);
            if (romAddress >= 0 && romAddress < ROM.Length) return ROM[romAddress];
            return 0xFF;
        }
        private byte ReadRAM(ushort address)
        {
            if(!ramEnabled || externalRAM == null || externalRAM.Length == 0) return 0xFF;
            int ramAddress = ramBank * 0x2000 + (address - 0xA000);
            if (ramAddress >= 0 && ramAddress < externalRAM.Length) return externalRAM[ramAddress];
            return 0xFF;
        }
        private void WriteControl(ushort address, byte value)
        {
            if (address <= 0x1FFF) EnableRAM(value);
            else if (address <= 0x2FFF) SetROMLow(value);
            else if (address <= 0x3FFF) SetROMHigh(value);
            else if (address <= 0x5FFF) SetRAMBank(value);
        }
        private void EnableRAM(byte value)
        {
            ramEnabled = (value & 0x0F) == 0x0A;
        }
        private void SetROMLow(byte value)
        {
            romBank = (romBank & 0x100) | value;
        }
        private void SetROMHigh(byte value)
        {
            romBank = (romBank & 0xFF) | ((value & 0x01) << 8);
        }
        private void SetRAMBank(byte value)
        {
            ramBank = value & 0x0F;
        }
        private void WriteRAM(ushort address, byte value)
        {
            if (!ramEnabled || externalRAM == null || externalRAM.Length == 0) return;
            int ramAddress = ramBank * 0x2000 + (address - 0xA000);
            if(ramAddress >= 0 && ramAddress < externalRAM.Length) externalRAM[ramAddress] = value;
        }
        public void Save(BinaryWriter writer)
        {
            writer.Write(romBank);
            writer.Write(ramBank);
            writer.Write(ramEnabled);
        }
        public void Load(BinaryReader reader)
        {
            romBank = reader.ReadInt32();
            ramBank = reader.ReadInt32();
            ramEnabled = reader.ReadBoolean();
        }
    }
}
